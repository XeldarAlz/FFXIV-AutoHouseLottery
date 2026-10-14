using System.Collections.Immutable;
using System.Threading.Tasks;
using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Providers.Model;
using AutoHouseLottery.Core.Selection.Model;

namespace AutoHouseLottery.Core.Find;

// The Find a home page's state: the selection (area, filters and the one shared shortlist), kept in the settings and
// the shortlist file. Update runs every frame on the framework thread and only compares a few numbers; when one changes
// while the window is open, the whole view is rebuilt on a task and swapped in when it is done. Only the windows read the
// view, so a closed window costs no rebuilds.
internal sealed class FindState
{
    private const string ShortlistName = "Shortlist";

    private readonly Configuration configuration;
    private readonly ShortlistStore store;
    private readonly FindSelection selection;
    private bool shortlistLoaded;
    private int savedShortlistRevision;
    private int viewRevision;
    private ViewInputs lastInputs;
    private Task<FindView>? building;

    public FindState(Configuration configuration, ShortlistStore store)
    {
        this.configuration = configuration;
        this.store = store;
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

    public uint WorldFor(CharacterProfile? character) => configuration.Find.WorldId != 0 ? configuration.Find.WorldId : character?.HomeWorldId ?? 0;

    public void Update(DateTime nowUtc, CharacterProfile? character, DiscoverySnapshot snapshot, bool windowOpen)
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

        var world = WorldFor(character);
        var inputs = new ViewInputs(
            snapshot.PlotsRevision,
            selection.QueryRevision,
            Shortlist.Revision,
            CharacterKey.Of(character),
            world,
            configuration.Find.GilReserve,
            nowUtc.Ticks / TimeSpan.TicksPerMinute);
        if (!windowOpen || building is not null || inputs == lastInputs)
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
            nowUtc);
        building = Task.Run(() => Build(request));
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
        };
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

    private readonly record struct ViewInputs(int PlotsRevision, int QueryRevision, int ShortlistRevision, CharacterKey? Character, uint WorldId, long GilReserve, long Minute);

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
        DateTime NowUtc);
}
