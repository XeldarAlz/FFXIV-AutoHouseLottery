using System.Collections.Immutable;
using System.IO;
using System.Runtime.InteropServices;
using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Integration;
using AutoHouseLottery.Core.Integration.Model;
using AutoHouseLottery.Core.Maps;
using AutoHouseLottery.Core.Providers;
using AutoHouseLottery.Core.Providers.Model;
using AutoHouseLottery.Core.Saved;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;

namespace AutoHouseLottery.Core.Find;

// Owns the housing discovery services and drives them from the framework update: the character and the Find a home view
// every frame, which only compares numbers, and the listings and saved notices once a second. Nothing here acts in game.
// The feed service, which holds the one plot store and lottery calendar, is the plugin's, shared with the housing context.
internal sealed class Discovery : IDisposable
{
    private const long TickIntervalMs = 1_000;
    private const string CacheFolder = "housing-cache";
    private const string ScheduleFile = "lottery-schedules.json";

    private readonly Func<bool> windowOpen;
    private readonly Configuration configuration;
    private readonly CurrentCharacter character;
    private readonly NoticeAnnouncer announcer;
    private readonly List<uint> watched = [];
    private ImmutableArray<uint> scope = [];
    private (uint World, int Library, int Shortlist) scopeInputs = (uint.MaxValue, -1, -1);
    private long nextTickMs;

    public Discovery(Configuration configuration, string directory, HousingFeedService feed, CurrentCharacter character, Func<PlanningJournal> journal, Func<bool> windowOpen)
    {
        this.windowOpen = windowOpen;
        this.configuration = configuration;
        this.character = character;
        Names = new GameNames(Svc.Data);
        Maps = new HousingGameMaps(Svc.Data);
        Feed = feed;
        Find = new FindState(configuration, new ShortlistStore(directory), journal);
        Saved = new SavedService(directory);
        announcer = new NoticeAnnouncer(configuration, Names);
        Svc.Framework.Update += OnUpdate;
    }

    public HousingFeedService Feed { get; }

    public static HousingFeedService CreateFeed(string directory, string version)
        => new(new HousingFeedClient(version), new HousingFeedCache(Path.Combine(directory, CacheFolder)), Path.Combine(directory, ScheduleFile));

    public FindState Find { get; }

    public SavedService Saved { get; }

    public HousingGameMaps Maps { get; }

    public GameNames Names { get; }

    public CharacterProfile? Character { get; private set; }

    public uint World => Find.WorldFor(Character);

    public void Dispose()
    {
        Svc.Framework.Update -= OnUpdate;
        Find.Close();
        Saved.Close();
    }

    private void OnUpdate(IFramework framework)
    {
        var now = DateTime.UtcNow;
        Character = character.Read();
        var snapshot = Feed.Snapshot;
        Find.Update(now, Character, snapshot);
        if (Environment.TickCount64 < nextTickMs)
        {
            return;
        }

        nextTickMs = Environment.TickCount64 + TickIntervalMs;
        if (Character is { } loggedIn)
        {
            configuration.RememberFacts(loggedIn.ContentId, loggedIn.ObservedGil, loggedIn.FreeCompanyId, loggedIn.AccountOwnsLandOnHomeWorld, now);
        }

        Feed.Tick(now, Mode(), Scope());
        Saved.Update(now, snapshot, announcer.Announce);
    }

    private RefreshMode Mode()
    {
        if (windowOpen())
        {
            return RefreshMode.Active;
        }

        return Saved.Library.IsEmpty && Find.Shortlist.Count == 0 ? RefreshMode.Paused : RefreshMode.Idle;
    }

    private ImmutableArray<uint> Scope()
    {
        var inputs = (World, Library: Saved.Library.Revision, Shortlist: Find.Shortlist.Revision);
        if (inputs == scopeInputs)
        {
            return scope;
        }

        scopeInputs = inputs;
        watched.Clear();
        Saved.Library.CollectWorlds(watched);
        var shortlist = Find.Shortlist.Plots;
        for (var index = 0; index < shortlist.Count; index++)
        {
            if (!watched.Contains(shortlist[index].WorldId))
            {
                watched.Add(shortlist[index].WorldId);
            }
        }

        scope = FetchScope.Worlds(inputs.World, CollectionsMarshal.AsSpan(watched));
        return scope;
    }
}
