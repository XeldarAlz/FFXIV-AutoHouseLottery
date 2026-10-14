using System.Collections.Frozen;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence;
using AutoHouseLottery.Core.Providers.Model;
using AutoHouseLottery.Core.Storage;

namespace AutoHouseLottery.Core.Providers;

// Ported from Aetherphone's housing service and reworked around the plot store. Tick runs on the framework thread and
// only decides; every request, parse, merge and disk write happens on a task, and the page reads the immutable snapshot
// published after each change without taking any lock. Parsing and normalizing hold only the calendar lock; the store
// lock covers the merge and the publish. Refreshing never touches the game, so it never takes the execution lease.
internal sealed class HousingFeedService : IDisposable
{
    private const int MaximumPositions = 8;

    private readonly HousingFeedClient client;
    private readonly HousingFeedCache cache;
    private readonly QueuedFile scheduleFile;
    private readonly Task<LotterySchedules> schedulesLoading;
    private readonly Lock gate = new();
    private readonly Lock calendarGate = new();
    private readonly PlotStore store = new();
    private readonly FeedSession session = new();
    private readonly Dictionary<uint, WorldFeedState> worlds = [];
    private readonly List<CyclePosition> positions = [];
    private readonly CancellationTokenSource shutdown = new();
    private ImmutableArray<FeedWorld> worldList = [];
    private FrozenDictionary<(uint World, ushort District), CoverageSnapshot> coverage = FrozenDictionary<(uint World, ushort District), CoverageSnapshot>.Empty;
    private int coverageRevision = -1;
    private LotterySchedules? schedules;
    private LotterySchedule calendar = LotteryCalendar.Global;
    private bool worldListRequested;
    private int revision;
    private volatile bool disposed;
    private volatile DiscoverySnapshot snapshot = DiscoverySnapshot.Empty;

    public HousingFeedService(HousingFeedClient client, HousingFeedCache cache, string schedulePath)
    {
        this.client = client;
        this.cache = cache;
        scheduleFile = new QueuedFile(schedulePath, "lottery calendar", static (exception, message) => RunLog.Warning(exception, message));
        schedulesLoading = Task.Run(() => LoadSchedules(schedulePath));
    }

    public DiscoverySnapshot Snapshot => snapshot;

    public void Tick(DateTime nowUtc, RefreshMode mode, ImmutableArray<uint> fetchScope)
    {
        if (mode == RefreshMode.Paused || !schedulesLoading.IsCompleted)
        {
            return;
        }

        RequestWorldList();
        lock (gate)
        {
            TakeSchedulesLocked();
            for (var index = 0; index < fetchScope.Length; index++)
            {
                var worldId = fetchScope[index];
                var state = StateFor(worldId);
                if (state.NeedsCacheRead)
                {
                    state.BeginCacheRead();
                    PublishLocked();
                    _ = Task.Run(() => ReadCache(worldId));
                    continue;
                }

                if (state.CanFetch(mode, nowUtc))
                {
                    BeginFetch(worldId, state, nowUtc);
                }
            }
        }
    }

    public bool RefreshNow(uint worldId, DateTime nowUtc)
    {
        lock (gate)
        {
            var state = StateFor(worldId);
            if (schedules is null || !state.CanRefreshNow(nowUtc))
            {
                return false;
            }

            RunLog.Info($"Refreshing the housing listings for world {worldId} on request.");
            BeginFetch(worldId, state, nowUtc);
            return true;
        }
    }

    // A placard read live in game reports the phase it is in and when it ends, which may anchor the calendar like the
    // listings' vote does. Returns false until the saved calendar has loaded, and when the calendar rejects it.
    public bool TryObservePhase(LotteryPhase phase, DateTime endsAtUtc, out CyclePosition position)
    {
        position = default;
        lock (gate)
        {
            if (!TakeSchedulesLocked())
            {
                return false;
            }

            ScheduleUpdate update;
            lock (calendarGate)
            {
                update = schedules!.Observe(GameRegion.Global, phase, endsAtUtc, out position);
                schedules.TryGet(GameRegion.Global, out calendar);
            }

            if (update == ScheduleUpdate.Rejected)
            {
                return false;
            }

            Adopt(update, "A placard", [position]);
            PublishLocked();
            return true;
        }
    }

    // Plot observations read in game (a placard, an acknowledged ward scan) go into the same store as the listings.
    public MergeRejection MergeLocal(in PlotObservation observation)
    {
        lock (gate)
        {
            var rejection = store.Merge(observation);
            if (rejection == MergeRejection.None)
            {
                PublishLocked();
            }

            return rejection;
        }
    }

    public PlotBatchResult ApplyLocal(in CoverageSnapshot coverageSnapshot, ReadOnlySpan<PlotObservation> observations)
    {
        lock (gate)
        {
            var result = store.Apply(coverageSnapshot, observations);
            PublishLocked();
            return result;
        }
    }

    public void Dispose()
    {
        disposed = true;
        shutdown.Cancel();
        client.Dispose();
        scheduleFile.Close();
        shutdown.Dispose();
    }

    private WorldFeedState StateFor(uint worldId)
    {
        if (!worlds.TryGetValue(worldId, out var state))
        {
            state = new WorldFeedState();
            worlds[worldId] = state;
        }

        return state;
    }

    private void BeginFetch(uint worldId, WorldFeedState state, DateTime nowUtc)
    {
        var ticket = session.Issue(worldId);
        state.Begin(ticket, nowUtc);
        PublishLocked();
        _ = Task.Run(() => FetchAsync(ticket));
    }

    private void ReadCache(uint worldId)
    {
        var cached = cache.ReadWorld(worldId, DateTime.UtcNow);
        var read = cached is { } fetch ? Read(fetch, worldId, CalendarUse.ReadOnly) : default;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            var result = FeedIntakeResult.Ignored(FeedIntakeStatus.FetchFailed);
            if (read.Feed is not null)
            {
                result = FeedIntake.Apply(store, session, session.Issue(worldId), read);
            }

            StateFor(worldId).FinishCacheRead(cached, result);
            if (result.Applied)
            {
                Remember(result.Feed!);
                RunLog.Info($"Loaded the saved housing listings for world {worldId} from {cached!.Value.Receipt.ReceivedUtc:u}: {result.Merged} plots.");
            }

            PublishLocked();
        }
    }

    private async Task FetchAsync(FeedTicket ticket)
    {
        FeedFetch fetch;
        try
        {
            fetch = await client.GetAsync(HousingEndpoints.World(ticket.WorldId), shutdown.Token).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        if (disposed || fetch.Status == FetchStatus.Cancelled)
        {
            return;
        }

        var read = Read(fetch, ticket.WorldId, CalendarUse.Anchor);
        FeedIntakeResult result;
        FetchOutcome outcome;
        DateTime retryAt;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            result = FeedIntake.Apply(store, session, ticket, read);
            var state = StateFor(ticket.WorldId);
            outcome = state.Finish(ticket, fetch, result, DateTime.UtcNow, Random.Shared.NextDouble());
            retryAt = state.RetryAt;
            if (result.Applied)
            {
                Remember(result.Feed!);
            }

            PublishLocked();
        }

        Report(ticket.WorldId, fetch, result, outcome, retryAt);
        if (outcome == FetchOutcome.Applied && !disposed)
        {
            cache.WriteWorld(ticket.WorldId, fetch.Body!, fetch.Receipt);
        }
    }

    private FeedRead Read(in FeedFetch fetch, uint worldId, CalendarUse use)
    {
        lock (calendarGate)
        {
            return FeedIntake.Read(fetch, worldId, schedules!, use);
        }
    }

    private void RequestWorldList()
    {
        lock (gate)
        {
            if (worldListRequested)
            {
                return;
            }

            worldListRequested = true;
        }

        _ = Task.Run(LoadWorldListAsync);
    }

    private async Task LoadWorldListAsync()
    {
        if (cache.ReadWorldList(DateTime.UtcNow) is { } cachedBody)
        {
            PublishWorldList(FeedNormalizer.ParseWorlds(cachedBody));
        }

        FeedFetch fetch;
        try
        {
            fetch = await client.GetAsync(HousingEndpoints.Worlds(), shutdown.Token).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        var parsed = fetch.Succeeded ? FeedNormalizer.ParseWorlds(fetch.Body) : [];
        if (parsed.IsEmpty)
        {
            RunLog.Warning($"The housing service's world list could not be loaded ({fetch.Status}, HTTP {fetch.HttpStatus}); the world picker keeps your home world.");
            return;
        }

        PublishWorldList(parsed);
        if (!disposed)
        {
            cache.WriteWorldList(fetch.Body!, fetch.Receipt);
        }
    }

    private void PublishWorldList(ImmutableArray<FeedWorld> parsed)
    {
        if (parsed.IsEmpty)
        {
            return;
        }

        lock (gate)
        {
            worldList = parsed;
            PublishLocked();
        }
    }

    private bool TakeSchedulesLocked()
    {
        if (schedules is not null)
        {
            return true;
        }

        if (!schedulesLoading.IsCompleted)
        {
            return false;
        }

        schedules = schedulesLoading.Result;
        schedules.TryGet(GameRegion.Global, out calendar);
        return true;
    }

    private void Remember(WorldFeed feed)
    {
        calendar = feed.Calendar;
        Adopt(feed.Schedule, "The listings", feed.Positions.AsSpan());
    }

    // The calendar the source moved is written out once, and the positions it proved join the few kept for the cycle.
    private void Adopt(ScheduleUpdate update, string source, ReadOnlySpan<CyclePosition> proved)
    {
        if (update is ScheduleUpdate.Established or ScheduleUpdate.Reanchored)
        {
            RunLog.Info($"{source} {(update == ScheduleUpdate.Established ? "established" : "moved")} the lottery calendar: cycle {calendar.AnchorCycleNumber} entry opens {calendar.AnchorEntryStartUtc:u} (revision {calendar.Revision}).");
            var persisted = calendar;
            scheduleFile.Write(() => LotteryScheduleFile.Serialize([persisted]));
        }

        for (var index = 0; index < proved.Length; index++)
        {
            if (positions.Contains(proved[index]))
            {
                continue;
            }

            positions.Add(proved[index]);
            if (positions.Count > MaximumPositions)
            {
                positions.RemoveAt(0);
            }
        }
    }

    private void PublishLocked()
    {
        revision++;
        var statuses = new Dictionary<uint, FeedWorldStatus>(worlds.Count);
        foreach (var pair in worlds)
        {
            statuses[pair.Key] = pair.Value.Status();
        }

        snapshot = new DiscoverySnapshot
        {
            Revision = revision,
            PlotsRevision = store.Revision,
            Plots = store.Plots,
            Positions = [.. positions],
            Worlds = statuses.ToFrozenDictionary(),
            Coverage = CoverageLocked(),
            WorldList = worldList,
            Schedule = calendar,
        };
    }

    private FrozenDictionary<(uint World, ushort District), CoverageSnapshot> CoverageLocked()
    {
        if (coverageRevision == store.Revision)
        {
            return coverage;
        }

        coverageRevision = store.Revision;
        var found = new Dictionary<(uint World, ushort District), CoverageSnapshot>();
        foreach (var worldId in worlds.Keys)
        {
            for (var index = 0; index < HousingDistricts.All.Length; index++)
            {
                var districtId = HousingDistricts.All[index].TerritoryId;
                if (CoverageScope.TryForDistrict(GameRegion.Global, worldId, districtId, out var scope) && store.TryGetCoverage(scope, ObservationSource.PublicFeed, out var snapshotOfDistrict))
                {
                    found[(worldId, districtId)] = snapshotOfDistrict;
                }
            }
        }

        coverage = found.ToFrozenDictionary();
        return coverage;
    }

    private static LotterySchedules LoadSchedules(string path)
    {
        if (LotteryScheduleFile.TryLoad(path, out var persisted))
        {
            return LotterySchedules.Create(persisted.AsSpan());
        }

        RunLog.Warning($"The saved lottery calendar in {Path.GetFileName(path)} could not be read; the built-in calendar is used until the listings confirm it.");
        return LotterySchedules.Create([]);
    }

    private static void Report(uint worldId, in FeedFetch fetch, in FeedIntakeResult result, FetchOutcome outcome, DateTime retryAt)
    {
        switch (outcome)
        {
            case FetchOutcome.Applied:
                RunLog.Info($"Housing listings for world {worldId} refreshed: {result.Merged} plots merged, {result.Rejected} rejected (first reason {result.FirstRejection}), {result.MarkedNotReported} no longer listed (service read {fetch.Receipt.UpstreamUtc:u}).");
                break;
            case FetchOutcome.Superseded:
                RunLog.Debug($"A housing response for world {worldId} was overtaken by a newer request and was not used.");
                break;
            default:
                RunLog.Warning($"Housing listings for world {worldId} were not refreshed ({fetch.Status}, {result.Status}, HTTP {fetch.HttpStatus}); the previous listings stay and the next try is at {retryAt:u}.");
                break;
        }
    }
}
