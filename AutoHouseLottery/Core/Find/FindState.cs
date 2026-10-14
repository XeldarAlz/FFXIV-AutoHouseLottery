using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Integration;
using AutoHouseLottery.Core.Integration.Model;
using AutoHouseLottery.Core.Providers.Model;
using AutoHouseLottery.Core.Selection.Model;

namespace AutoHouseLottery.Core.Find;

// The Find a home page's state: the selection (area, filters and the one shared shortlist), kept in the settings and
// the shortlist file, and the character roster with each character's batch settings. Update runs every frame on the
// framework thread and only compares a few numbers; when one changes, the whole view is rebuilt on a task and swapped in
// when it is done. The roster is copied out of the settings on the framework thread, so the task never reads them.
internal sealed class FindState
{
    private const string ShortlistName = "Shortlist";

    private readonly Configuration configuration;
    private readonly ShortlistStore store;
    private readonly FindSelection selection;
    private readonly Func<PlanningJournal> journal;
    private ImmutableArray<RosterEntry> roster = [];
    private int selectedCount;
    private (int Revision, ulong LoggedIn) rosterBuiltFor = (-1, 0);
    private (int Roster, int Shortlist, PlacementChoice Placement)? multipleWinsAcceptedFor;
    private bool shortlistLoaded;
    private int savedShortlistRevision;
    private int viewRevision;
    private ViewInputs lastInputs;
    private Task<FindView>? building;

    public FindState(Configuration configuration, ShortlistStore store, Func<PlanningJournal> journal)
    {
        this.configuration = configuration;
        this.store = store;
        this.journal = journal;
        selection = LoadSelection(configuration.Find);
    }

    public FindView View { get; private set; } = FindView.Empty;

    public Shortlist Shortlist => selection.Shortlist;

    public AreaScope Area => selection.Area;

    public PlotFilter Filter => selection.Filter;

    public ResultSort Sort => selection.Sort;

    public bool ListView => configuration.Find.ListView;

    public long GilReserve => configuration.Find.GilReserve;

    public uint ChosenWorld => configuration.Find.WorldId;

    public int QueryRevision => selection.QueryRevision;

    // Every known character, the logged-in one first, with what the player chose for it.
    public ImmutableArray<RosterEntry> Roster => roster;

    public int SelectedCount => selectedCount;

    public int RosterRevision => configuration.RosterRevision;

    public PlacementChoice Placement => (PlacementChoice)configuration.BatchPlacement;

    // The player's confirmation that only one win per ownership group can be claimed. It holds only for the roster,
    // shortlist and placement it was given for, so any change to the plan asks again.
    public bool MultipleWinRiskAccepted => multipleWinsAcceptedFor == (configuration.RosterRevision, Shortlist.Revision, Placement);

    public uint WorldFor(CharacterProfile? character) => configuration.Find.WorldId != 0 ? configuration.Find.WorldId : character?.HomeWorldId ?? 0;

    public void Update(DateTime nowUtc, CharacterProfile? character, DiscoverySnapshot snapshot)
    {
        TakeLoadedShortlist();
        SaveShortlistIfChanged();
        if (building is { IsCompleted: true } finished)
        {
            building = null;
            if (finished.IsCompletedSuccessfully)
            {
                View = finished.Result;
            }
            else
            {
                RunLog.Error(finished.Exception!.GetBaseException(), "Building the Find a home results failed; the previous results stay");
            }
        }

        RefreshRoster(character?.ContentId ?? 0);
        var world = WorldFor(character);
        var journalSnapshot = journal();
        var inputs = new ViewInputs(
            snapshot.PlotsRevision,
            selection.QueryRevision,
            Shortlist.Revision,
            CharacterKey.Of(character),
            world,
            configuration.Find.GilReserve,
            nowUtc.Ticks / TimeSpan.TicksPerMinute,
            configuration.RosterRevision,
            MultipleWinRiskAccepted,
            journalSnapshot);
        if (building is not null || inputs == lastInputs)
        {
            return;
        }

        lastInputs = inputs;
        viewRevision++;
        var request = new ViewRequest(
            viewRevision,
            snapshot,
            selection.Area with { WorldId = world },
            selection.Filter,
            selection.Sort,
            selection.PreferredDistricts,
            Shortlist.Freeze(),
            character,
            configuration.Find.GilReserve,
            selection.QueryRevision,
            nowUtc,
            roster,
            journalSnapshot,
            Placement,
            MultipleWinRiskAccepted,
            configuration.RosterRevision);
        building = Task.Run(() => Build(request));
    }

    public void SetSelected(ulong contentId, bool chosen) => SetChoice(contentId, ChoiceOf(contentId) with { Selected = chosen });

    public void SetBuyer(ulong contentId, BuyerType buyer) => SetChoice(contentId, ChoiceOf(contentId) with { Buyer = (byte)buyer });

    public void SetDepositCap(ulong contentId, long cap) => SetChoice(contentId, ChoiceOf(contentId) with { DepositCap = Math.Max(0, cap) });

    public void SetReserve(ulong contentId, long? reserve) => SetChoice(contentId, ChoiceOf(contentId) with { Reserve = reserve is { } amount ? Math.Max(0, amount) : null });

    public void SetTarget(ulong contentId, PlotKey? target)
        => SetChoice(contentId, ChoiceOf(contentId) with { TargetDistrict = target?.DistrictId ?? 0, TargetWard = target?.Ward ?? 0, TargetPlot = target?.Plot ?? 0 });

    public void SetAccountGroup(ulong contentId, int? accountGroup)
    {
        configuration.SetAccountGroup(contentId, accountGroup);
        RunLog.Info($"Account group of character {contentId:X} set to {accountGroup?.ToString() ?? "none"}.");
    }

    public void SetPlacement(PlacementChoice placement) => configuration.SetBatchPlacement((byte)placement);

    public void AcceptMultipleWins(bool accepted)
    {
        multipleWinsAcceptedFor = accepted ? (configuration.RosterRevision, Shortlist.Revision, Placement) : null;
        RunLog.Info(accepted ? "The player confirmed that only one win per ownership group can be claimed for this plan." : "The multiple-win confirmation was withdrawn.");
    }

    private static int CompareRoster(in RosterEntry left, in RosterEntry right, ulong loggedIn)
    {
        if (left.ContentId == right.ContentId)
        {
            return 0;
        }

        if (left.ContentId == loggedIn || right.ContentId == loggedIn)
        {
            return left.ContentId == loggedIn ? -1 : 1;
        }

        var byName = string.Compare(left.Facts.Name, right.Facts.Name, StringComparison.CurrentCulture);
        return byName != 0 ? byName : left.ContentId.CompareTo(right.ContentId);
    }

    private RosterChoice ChoiceOf(ulong contentId) => configuration.ChoiceFor(contentId);

    private void SetChoice(ulong contentId, RosterChoice choice) => configuration.SetChoice(contentId, choice);

    // Copied from the settings whenever they change or another character logs in: the logged-in character first, then
    // the rest by name. A target is a plot on the character's own home world.
    private void RefreshRoster(ulong loggedIn)
    {
        var key = (configuration.RosterRevision, loggedIn);
        if (key == rosterBuiltFor)
        {
            return;
        }

        rosterBuiltFor = key;
        var entries = new List<RosterEntry>(configuration.Characters.Count);
        foreach (var (contentId, known) in configuration.Characters)
        {
            var choice = configuration.ChoiceFor(contentId);
            PlotKey? target = choice.TargetDistrict != 0 && PlotKey.TryCreate(HousingContext.Region, known.HomeWorldId, choice.TargetDistrict, choice.TargetWard, choice.TargetPlot, out var plot)
                ? plot
                : null;
            var selection = new RosterSelection(choice.Selected, (BuyerType)choice.Buyer, choice.DepositCap, choice.Reserve, target);
            entries.Add(new RosterEntry(HousingContext.FactsOf(contentId, known), selection) { ManualRefunds = !configuration.AutomaticRefunds(contentId) });
        }

        entries.Sort((left, right) => CompareRoster(left, right, loggedIn));
        roster = [.. entries];
        selectedCount = 0;
        for (var index = 0; index < roster.Length; index++)
        {
            selectedCount += roster[index].Selection.Selected ? 1 : 0;
        }
    }

    public void SetWorld(uint worldId)
    {
        configuration.Find.WorldId = worldId;
        selection.WorldChanged();
        configuration.SaveDebounced();
    }

    public void SetArea(in AreaScope area)
    {
        var settings = configuration.Find;
        var districts = area.DistrictIds();
        if (districts.Length == HousingDistricts.All.Length)
        {
            settings.Districts = null;
        }
        else
        {
            settings.Districts = new List<uint>(districts.Length);
            for (var index = 0; index < districts.Length; index++)
            {
                settings.Districts.Add(districts[index]);
            }
        }

        settings.AllWards = area.AllWards;
        settings.Wards = area.AllWards ? [] : [.. area.WardNumbers()];
        settings.MainDivision = area.MainDivision;
        settings.Subdivision = area.Subdivision;
        selection.SetArea(area with { WorldId = 0 });
        configuration.SaveDebounced();
    }

    public void SetFilter(in PlotFilter filter)
    {
        var settings = configuration.Find;
        settings.Sizes = (byte)filter.Sizes;
        settings.Buyer = (byte)filter.Buyer;
        settings.PriceCeiling = filter.PriceCeiling;
        settings.Statuses = (byte)filter.Statuses;
        settings.FirstPlot = filter.FirstPlot;
        settings.LastPlot = filter.LastPlot;
        settings.MinimumEntries = filter.MinimumEntries;
        settings.MaximumEntries = filter.MaximumEntries;
        settings.MaximumAgeHours = filter.MaximumAgeHours;
        selection.SetFilter(filter);
        configuration.SaveDebounced();
    }

    public void SetSort(ResultSort sort)
    {
        configuration.Find.Sort = (byte)sort;
        selection.SetSort(sort);
        configuration.SaveDebounced();
    }

    public void SetListView(bool listView)
    {
        configuration.Find.ListView = listView;
        configuration.SaveDebounced();
    }

    public void SetGilReserve(long reserve)
    {
        configuration.Find.GilReserve = Math.Max(0, reserve);
        configuration.SaveDebounced();
    }

    public void TogglePreferred(uint districtId)
    {
        var preferred = configuration.Find.PreferredDistricts;
        if (!preferred.Remove(districtId))
        {
            preferred.Add(districtId);
        }

        selection.SetPreferred(AreaScope.DistrictMaskOf([.. preferred]));
        configuration.SaveDebounced();
    }

    public bool IsPreferred(uint districtId) => (selection.PreferredDistricts & AreaScope.DistrictBit(districtId)) != 0;

    public void ApplySearch(in AreaScope area, in PlotFilter filter)
    {
        configuration.Find.WorldId = area.WorldId;
        SetArea(area);
        SetFilter(filter);
    }

    public bool Toggle(PlotKey key)
    {
        var added = selection.Toggle(key);
        RunLog.Info(added ? $"Added {key} to the shortlist." : $"Removed {key} from the shortlist.");
        return added;
    }

    public int AddAll(ImmutableArray<PlotKey> keys, string origin)
    {
        var added = selection.AddAll(keys);
        RunLog.Info($"{origin}: added {added} of {keys.Length} plots to the shortlist, which now holds {Shortlist.Count}.");
        return added;
    }

    public void Remove(PlotKey key)
    {
        if (selection.Remove(key))
        {
            RunLog.Info($"Removed {key} from the shortlist.");
        }
    }

    public int RemoveUnavailable()
    {
        var removed = selection.RemoveUnavailable(View.Shortlist);
        RunLog.Info($"Removed {removed} unavailable plots from the shortlist.");
        return removed;
    }

    public void ClearShortlist()
    {
        RunLog.Info($"Cleared the shortlist of {Shortlist.Count} plots.");
        Shortlist.Clear();
    }

    public void Close()
    {
        SaveShortlistIfChanged();
        store.Close();
    }

    private static FindView Build(ViewRequest request)
    {
        var plots = request.Snapshot.Plots;
        var position = CycleResolver.Current(request.Snapshot.Schedule, request.Snapshot.Positions.AsSpan(), request.NowUtc);
        var query = new FindQuery(request.Area, request.Filter, request.Sort, request.PreferredDistricts, position, request.NowUtc);
        var results = request.Area.IsEmpty ? FindResultsBuilder.Build(DiscoverySnapshot.Empty.Plots, query) : FindResultsBuilder.Build(plots, query);
        var rows = ShortlistRules.Rows(request.Shortlist.Plots, plots, position);
        RunPreview? preview = null;
        RunPolicy? policy = null;
        if (request.Character is { } character && !request.Shortlist.Plots.IsEmpty)
        {
            policy = RunPreviewBuilder.BestMatchPolicy(character, request.Filter.Buyer, request.Filter.PriceCeiling, request.GilReserve, request.Shortlist, position);
            preview = AssignmentPlanner.Build(policy, [character], plots, request.NowUtc);
        }

        var ranking = preview is { PolicyError: RunPolicyError.None } planned
            ? planned.Ranking
            : BestMatchRanking.Rank(request.Shortlist.Plots.AsSpan(), plots, position);
        var rosterProfiles = RosterProfilesOf(request, position);
        var batch = request.Shortlist.Plots.IsEmpty ? null : BuildBatch(request, rosterProfiles, plots, position);
        return new FindView
        {
            Revision = request.Revision,
            Results = results,
            Plots = plots,
            Position = position,
            AreaCovered = AreaCoverage.Covered(request.Snapshot.Coverage, request.Area),
            Shortlist = rows,
            ShortlistRevision = request.Shortlist.Revision,
            QueryRevision = request.QueryRevision,
            BestMatch = BestMatchOrder.For(preview, ranking),
            Preview = preview,
            PreviewPolicy = preview is { PolicyError: RunPolicyError.None } ? policy : null,
            UnavailableCount = ShortlistRules.Unavailable(rows).Length,
            Batch = batch,
            RosterProfiles = rosterProfiles,
        };
    }

    // Every known character as the planner sees it: the logged-in one from its live profile, the others from their last
    // login and the journal. In roster order.
    private static ImmutableArray<CharacterProfile> RosterProfilesOf(ViewRequest request, in CyclePosition position)
    {
        var roster = request.Roster;
        var profiles = new CharacterProfile[roster.Length];
        for (var index = 0; index < profiles.Length; index++)
        {
            profiles[index] = request.Character is { } live && live.ContentId == roster[index].ContentId
                ? live
                : RosterProfiles.Offline(roster[index].Facts, HousingContext.Region, request.Journal, position);
        }

        return ImmutableCollectionsMarshal.AsImmutableArray(profiles);
    }

    private static BatchView? BuildBatch(ViewRequest request, ImmutableArray<CharacterProfile> rosterProfiles, IReadOnlyDictionary<PlotKey, PlotSnapshot> plots, in CyclePosition position)
    {
        var chosen = new List<RosterEntry>();
        var profiles = new List<CharacterProfile>();
        for (var index = 0; index < request.Roster.Length; index++)
        {
            if (request.Roster[index].Selection.Selected)
            {
                chosen.Add(request.Roster[index]);
                profiles.Add(rosterProfiles[index]);
            }
        }

        var selected = CollectionsMarshal.AsSpan(chosen);
        if (!RunPreviewBuilder.IsBatch(selected))
        {
            return null;
        }

        var policy = RunPreviewBuilder.BatchPolicy(selected, request.Filter.Buyer, request.Filter.PriceCeiling, request.GilReserve, request.Shortlist, position, request.Placement, request.MultipleWinRiskAccepted);
        var preview = AssignmentPlanner.Build(policy, CollectionsMarshal.AsSpan(profiles), plots, request.NowUtc);
        return new BatchView(preview, preview.PolicyError == RunPolicyError.None ? policy : null, [.. profiles], request.RosterRevision);
    }

    private void TakeLoadedShortlist()
    {
        if (shortlistLoaded || !store.TryTakeLoaded(out var loaded))
        {
            return;
        }

        shortlistLoaded = true;
        var replaced = loaded is not null && selection.TakeLoaded(loaded);
        if (loaded is not null)
        {
            RunLog.Info($"Loaded the shortlist with {loaded.Count} plots.");
        }

        savedShortlistRevision = replaced || Shortlist.Count == 0 ? Shortlist.Revision : -1;
    }

    private void SaveShortlistIfChanged()
    {
        if (!shortlistLoaded || Shortlist.Revision == savedShortlistRevision)
        {
            return;
        }

        savedShortlistRevision = Shortlist.Revision;
        store.Save(Shortlist.Freeze(), ShortlistName);
    }

    private static FindSelection LoadSelection(FindSettings settings)
    {
        var districts = settings.Districts is { } chosen ? AreaScope.DistrictMaskOf([.. chosen]) : AreaScope.AllDistrictsMask;
        var area = new AreaScope(0, districts, settings.AllWards, AreaScope.WardMaskOf([.. settings.Wards]), settings.MainDivision, settings.Subdivision);
        var filter = PlotFilter.Sanitized(
            (SizeFilter)settings.Sizes,
            (BuyerType)settings.Buyer,
            settings.PriceCeiling,
            (StatusFilter)settings.Statuses,
            settings.FirstPlot,
            settings.LastPlot,
            settings.MinimumEntries,
            settings.MaximumEntries,
            settings.MaximumAgeHours);
        var sort = Enum.IsDefined((ResultSort)settings.Sort) ? (ResultSort)settings.Sort : ResultSort.PreferredAreas;
        var shortlist = new Shortlist(Guid.NewGuid(), ShortlistName);
        return new FindSelection(shortlist, area, filter, sort, AreaScope.DistrictMaskOf([.. settings.PreferredDistricts]));
    }

    private readonly record struct ViewInputs(
        int PlotsRevision,
        int QueryRevision,
        int ShortlistRevision,
        CharacterKey? Character,
        uint WorldId,
        long GilReserve,
        long Minute,
        int RosterRevision,
        bool MultipleWinRiskAccepted,
        PlanningJournal Journal);

    private sealed record ViewRequest(
        int Revision,
        DiscoverySnapshot Snapshot,
        AreaScope Area,
        PlotFilter Filter,
        ResultSort Sort,
        uint PreferredDistricts,
        ShortlistSnapshot Shortlist,
        CharacterProfile? Character,
        long GilReserve,
        int QueryRevision,
        DateTime NowUtc,
        ImmutableArray<RosterEntry> Roster,
        PlanningJournal Journal,
        PlacementChoice Placement,
        bool MultipleWinRiskAccepted,
        int RosterRevision);
}
