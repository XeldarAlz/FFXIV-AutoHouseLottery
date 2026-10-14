using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence;
using AutoHouseLottery.Core.Providers.Model;

namespace AutoHouseLottery.Core.Providers;

// Ported from Aetherphone's housing service and reworked around the plot store. Tick runs on the framework thread and
// only decides; every request, parse, merge and cache write happens on a task, and the page reads the immutable snapshot
// published after each merge. Refreshing never touches the game, so it never takes the execution lease.
internal sealed class HousingFeedService : IDisposable
{
    private const int MaximumPositions = 8;

    private readonly HousingFeedClient client;
    private readonly HousingFeedCache cache;
    private readonly string schedulePath;
    private readonly Task<LotterySchedules> schedulesLoading;
    private readonly Lock gate = new();
    private readonly PlotStore store = new();
    private readonly FeedSession session = new();
    private readonly Dictionary<uint, WorldState> worlds = [];
    private readonly List<CyclePosition> positions = [];
    private readonly CancellationTokenSource shutdown = new();
    private ImmutableArray<uint> scope = [];
    private ImmutableArray<FeedWorld> worldList = [];
    private bool worldListRequested;
    private int revision;
    private LotterySchedules? schedules;
    private volatile DiscoverySnapshot snapshot = DiscoverySnapshot.Empty;

    public HousingFeedService(HousingFeedClient client, HousingFeedCache cache, string schedulePath)
    {
        this.client = client;
        this.cache = cache;
        this.schedulePath = schedulePath;
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
            schedules ??= schedulesLoading.Result;
            scope = fetchScope;
            for (var index = 0; index < fetchScope.Length; index++)
            {
                var worldId = fetchScope[index];
                var state = StateFor(worldId);
                if (!state.CacheRead)
                {
                    BeginCacheRead(worldId, state);
                    continue;
                }

                if (!state.InFlight && RefreshCadence.IsDue(state.Timer, mode, nowUtc))
                {
                    BeginFetch(worldId, state, nowUtc);
                }
            }
        }
    }

    public TimeSpan ManualWait(uint worldId, DateTime nowUtc)
    {
        lock (gate)
        {
            return worlds.TryGetValue(worldId, out var state) ? RefreshCadence.ManualWait(state.Timer, nowUtc) : TimeSpan.Zero;
        }
    }

    public bool RefreshNow(uint worldId, DateTime nowUtc)
    {
        lock (gate)
        {
            var state = StateFor(worldId);
            if (schedules is null || !state.CacheRead || state.InFlight || RefreshCadence.ManualWait(state.Timer, nowUtc) > TimeSpan.Zero)
            {
                return false;
            }

            RunLog.Info($"Refreshing the housing listings for world {worldId} on request.");
            BeginFetch(worldId, state, nowUtc);
            return true;
        }
    }

    public void Dispose()
    {
        shutdown.Cancel();
        client.Dispose();
        shutdown.Dispose();
    }

    private WorldState StateFor(uint worldId)
    {
        if (!worlds.TryGetValue(worldId, out var state))
        {
            state = new WorldState();
            worlds[worldId] = state;
        }

        return state;
    }

    private void BeginCacheRead(uint worldId, WorldState state)
    {
        if (state.CacheReading)
        {
            return;
        }

        state.CacheReading = true;
        PublishLocked();
        _ = Task.Run(() => ReadCache(worldId), shutdown.Token);
    }

    private void BeginFetch(uint worldId, WorldState state, DateTime nowUtc)
    {
        state.InFlight = true;
        state.Timer = RefreshCadence.Started(state.Timer, nowUtc);
        var ticket = session.Issue(worldId);
        PublishLocked();
        _ = Task.Run(() => FetchAsync(ticket), shutdown.Token);
    }

    private void ReadCache(uint worldId)
    {
        var cached = cache.ReadWorld(worldId, DateTime.UtcNow);
        lock (gate)
        {
            var state = StateFor(worldId);
            state.CacheRead = true;
            state.CacheReading = false;
            if (cached is { } fetch)
            {
                var ticket = session.Issue(worldId);
                var result = FeedIntake.Accept(store, session, ticket, scope.AsSpan(), fetch, schedules!);
                if (result.Applied)
                {
                    state.HasData = true;
                    state.CachedReceivedUtc = fetch.Receipt.ReceivedUtc;
                    Remember(result.Feed!);
                    RunLog.Info($"Loaded the saved housing listings for world {worldId} from {fetch.Receipt.ReceivedUtc:u}: {result.Merged} plots.");
                }

                PublishLocked();
                return;
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

        if (fetch.Status == FetchStatus.Cancelled)
        {
            return;
        }

        FeedIntakeResult result;
        TimeSpan? retryIn = null;
        lock (gate)
        {
            var state = StateFor(ticket.WorldId);
            state.InFlight = false;
            result = FeedIntake.Accept(store, session, ticket, scope.AsSpan(), fetch, schedules!);
            var now = DateTime.UtcNow;
            if (result.Applied)
            {
                state.Timer = RefreshCadence.Succeeded(state.Timer, now, fetch.MaxAge);
                state.HasData = true;
                state.LiveReceivedUtc = fetch.Receipt.ReceivedUtc;
                state.LiveUpstreamUtc = fetch.Receipt.UpstreamUtc;
                state.Failure = FeedFailure.None;
                state.HttpStatus = fetch.HttpStatus;
                Remember(result.Feed!);
            }
            else if (result.Status != FeedIntakeStatus.Stale)
            {
                state.Timer = RefreshCadence.Failed(state.Timer, now, fetch.RetryAfter, Random.Shared.NextDouble());
                state.Failure = FailureOf(fetch.Status, result.Status);
                state.HttpStatus = fetch.HttpStatus;
                var retryAt = state.Timer.RetryAfterUtc > state.Timer.BackoffUntilUtc ? state.Timer.RetryAfterUtc : state.Timer.BackoffUntilUtc;
                retryIn = retryAt - now;
            }

            PublishLocked();
        }

        Report(ticket.WorldId, fetch, result, retryIn);
        if (result.Applied)
        {
            cache.WriteWorld(ticket.WorldId, fetch);
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

        _ = Task.Run(LoadWorldListAsync, shutdown.Token);
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
        cache.WriteWorldList(fetch);
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

    private void Remember(WorldFeed feed)
    {
        if (feed.Schedule is ScheduleUpdate.Established or ScheduleUpdate.Reanchored && schedules!.TryGet(GameRegion.Global, out var schedule))
        {
            RunLog.Info($"The listings {(feed.Schedule == ScheduleUpdate.Established ? "established" : "moved")} the lottery calendar: cycle {schedule.AnchorCycleNumber} entry opens {schedule.AnchorEntryStartUtc:u} (revision {schedule.Revision}).");
            var persisted = schedules.All;
            _ = Task.Run(() => SaveSchedules(persisted));
        }

        for (var index = 0; index < feed.Positions.Length; index++)
        {
            if (positions.Contains(feed.Positions[index]))
            {
                continue;
            }

            positions.Add(feed.Positions[index]);
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
            var state = pair.Value;
            statuses[pair.Key] = new FeedWorldStatus(
                state.InFlight || state.CacheReading,
                state.HasData,
                state.LiveReceivedUtc,
                state.LiveUpstreamUtc,
                state.CachedReceivedUtc,
                state.Failure,
                state.HttpStatus);
        }

        snapshot = new DiscoverySnapshot
        {
            Revision = revision,
            PlotsRevision = store.Revision,
            Plots = store.Plots,
            Positions = [.. positions],
            Worlds = statuses.ToFrozenDictionary(),
            WorldList = worldList,
            Schedule = schedules is not null && schedules.TryGet(GameRegion.Global, out var schedule) ? schedule : LotteryCalendar.Global,
        };
    }

    private static LotterySchedules LoadSchedules(string path)
    {
        if (LotteryScheduleFile.TryLoad(path, out var persisted))
        {
            return LotterySchedules.Create(persisted.AsSpan());
        }

        RunLog.Warning("The saved lottery calendar could not be read; the built-in calendar is used until the listings confirm it.");
        return LotterySchedules.Create([]);
    }

    private void SaveSchedules(ImmutableArray<LotterySchedule> persisted)
    {
        if (!LotteryScheduleFile.TrySave(schedulePath, persisted.AsSpan()))
        {
            RunLog.Warning("The lottery calendar could not be saved; it is confirmed again from the next listings.");
        }
    }

    private static void Report(uint worldId, in FeedFetch fetch, in FeedIntakeResult result, TimeSpan? retryIn)
    {
        switch (result.Status)
        {
            case FeedIntakeStatus.Applied:
                RunLog.Info($"Housing listings for world {worldId} refreshed: {result.Merged} plots merged, {result.Rejected} rejected (first reason {result.FirstRejection}), {result.MarkedNotReported} no longer listed (service read {fetch.Receipt.UpstreamUtc:u}).");
                break;
            case FeedIntakeStatus.Stale:
                RunLog.Debug($"A housing response for world {worldId} arrived after the page moved on and was ignored.");
                break;
            default:
                RunLog.Warning($"Housing listings for world {worldId} were not refreshed ({fetch.Status}, {result.Status}, HTTP {fetch.HttpStatus}); the previous listings stay and the next try is in {retryIn?.TotalSeconds:F0}s.");
                break;
        }
    }

    private static FeedFailure FailureOf(FetchStatus fetch, FeedIntakeStatus intake) => fetch switch
    {
        FetchStatus.Success => intake == FeedIntakeStatus.Applied ? FeedFailure.None : FeedFailure.Malformed,
        FetchStatus.RateLimited => FeedFailure.RateLimited,
        FetchStatus.HttpError => FeedFailure.HttpError,
        FetchStatus.Oversized => FeedFailure.Oversized,
        FetchStatus.TimedOut => FeedFailure.TimedOut,
        _ => FeedFailure.Unreachable,
    };

    private sealed class WorldState
    {
        public RefreshTimer Timer;
        public bool InFlight;
        public bool CacheRead;
        public bool CacheReading;
        public bool HasData;
        public DateTime LiveReceivedUtc;
        public DateTime LiveUpstreamUtc;
        public DateTime CachedReceivedUtc;
        public FeedFailure Failure;
        public int HttpStatus;
    }
}
