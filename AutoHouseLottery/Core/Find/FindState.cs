using System.Collections.Immutable;
using System.Threading.Tasks;
using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Providers.Model;
using AutoHouseLottery.Core.Selection.Model;

namespace AutoHouseLottery.Core.Find;

// The Find a home page's state: its area and filters, kept in the settings, and the one shortlist shared by the map,
// the list and the drawers. Update runs every frame on the framework thread and only compares a few numbers; when one
// changes, the whole view is rebuilt on a task and swapped in when it is done.
internal sealed class FindState
{
    private const string ShortlistName = "Shortlist";
    private const long TicksPerMinute = TimeSpan.TicksPerMinute;

    private readonly Configuration configuration;
    private readonly ShortlistStore store;
    private Shortlist shortlist = new(Guid.NewGuid(), ShortlistName);
    private bool shortlistLoaded;
    private int savedShortlistRevision;
    private ViewInputs lastInputs;
    private Task<FindView>? building;

    public FindState(Configuration configuration, ShortlistStore store)
    {
        this.configuration = configuration;
        this.store = store;
        LoadQuery();
    }

    public FindView View { get; private set; } = FindView.Empty;

    public Shortlist Shortlist => shortlist;

    public AreaScope Area { get; private set; }

    public PlotFilter Filter { get; private set; }

    public ResultSort Sort { get; private set; }

    public uint PreferredDistricts { get; private set; }

    public bool ListView => configuration.Find.ListView;

    public long GilReserve => configuration.Find.GilReserve;

    public uint ChosenWorld => configuration.Find.WorldId;

    public int QueryRevision { get; private set; }

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

        var world = WorldFor(character);
        var inputs = new ViewInputs(
            snapshot.PlotsRevision,
            QueryRevision,
            shortlist.Revision,
            character?.ContentId ?? 0,
            world,
            configuration.Find.GilReserve,
            nowUtc.Ticks / TicksPerMinute);
        if (building is not null || inputs == lastInputs)
        {
            return;
        }

        lastInputs = inputs;
        var request = new ViewRequest(snapshot, Area with { WorldId = world }, Filter, Sort, PreferredDistricts, shortlist.Freeze(), character, configuration.Find.GilReserve, QueryRevision, nowUtc);
        building = Task.Run(() => Build(request));
    }

    public void SetWorld(uint worldId)
    {
        configuration.Find.WorldId = worldId;
        Changed();
    }

    public void SetArea(in AreaScope area)
    {
        var settings = configuration.Find;
        var districts = area.DistrictIds();
        settings.Districts = districts.Length == HousingDistricts.All.Length ? null : [.. districts.Select(static district => (uint)district)];
        settings.AllWards = area.AllWards;
        settings.Wards = area.AllWards ? [] : [.. area.WardNumbers()];
        settings.MainDivision = area.MainDivision;
        settings.Subdivision = area.Subdivision;
        Area = area with { WorldId = 0 };
        Changed();
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
        Filter = filter;
        Changed();
    }

    public void SetSort(ResultSort sort)
    {
        configuration.Find.Sort = (byte)sort;
        Sort = sort;
        Changed();
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

        PreferredDistricts = AreaScope.DistrictMaskOf(preferred.ToArray());
        Changed();
    }

    public bool IsPreferred(uint districtId) => (PreferredDistricts & AreaScope.DistrictBit(districtId)) != 0;

    public void ApplySearch(in AreaScope area, in PlotFilter filter)
    {
        configuration.Find.WorldId = area.WorldId;
        SetArea(area);
        SetFilter(filter);
    }

    public bool Toggle(PlotKey key)
    {
        if (shortlist.Remove(key))
        {
            RunLog.Info($"Removed {key} from the shortlist.");
            return false;
        }

        shortlist.Add(key);
        RunLog.Info($"Added {key} to the shortlist.");
        return true;
    }

    public int AddAll(ImmutableArray<PlotKey> keys, string origin)
    {
        var added = shortlist.AddRange(keys.AsSpan());
        RunLog.Info($"{origin}: added {added} of {keys.Length} plots to the shortlist, which now holds {shortlist.Count}.");
        return added;
    }

    public void Remove(PlotKey key)
    {
        if (shortlist.Remove(key))
        {
            RunLog.Info($"Removed {key} from the shortlist.");
        }
    }

    public int RemoveUnavailable()
    {
        var unavailable = ShortlistRules.Unavailable(View.Shortlist);
        var removed = 0;
        for (var index = 0; index < unavailable.Length; index++)
        {
            if (shortlist.Remove(unavailable[index]))
            {
                removed++;
            }
        }

        RunLog.Info($"Removed {removed} unavailable plots from the shortlist.");
        return removed;
    }

    public void ClearShortlist()
    {
        RunLog.Info($"Cleared the shortlist of {shortlist.Count} plots.");
        shortlist.Clear();
    }

    public void Flush() => store.Flush();

    private static FindView Build(ViewRequest request)
    {
        var plots = request.Snapshot.Plots;
        var position = CycleResolver.Current(request.Snapshot.Schedule, request.Snapshot.Positions.AsSpan(), request.NowUtc);
        var query = new FindQuery(request.Area, request.Filter, request.Sort, request.PreferredDistricts, position, request.NowUtc);
        var results = request.Area.IsEmpty ? FindResultsBuilder.Build(DiscoverySnapshot.Empty.Plots, query) : FindResultsBuilder.Build(plots, query);
        var rows = ShortlistRules.Rows(request.Shortlist.Plots, plots, position);
        RunPreview? preview = null;
        if (request.Character is { } character && !request.Shortlist.Plots.IsEmpty)
        {
            preview = RunPreviewBuilder.Build(character, request.Filter.Buyer, request.Filter.PriceCeiling, request.GilReserve, request.Shortlist, position, plots, request.NowUtc);
        }

        var ranking = preview is { PolicyError: RunPolicyError.None } planned
            ? planned.Ranking
            : BestMatchRanking.Rank(request.Shortlist.Plots.AsSpan(), plots, position);
        return new FindView
        {
            Results = results,
            Plots = plots,
            Position = position,
            Shortlist = rows,
            ShortlistRevision = request.Shortlist.Revision,
            QueryRevision = request.QueryRevision,
            Ranking = ranking,
            Preview = preview,
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
        if (loaded is not null)
        {
            if (shortlist.Count == 0)
            {
                shortlist = loaded;
            }
            else
            {
                shortlist.AddRange([.. loaded.Plots]);
            }

            RunLog.Info($"Loaded the shortlist with {loaded.Count} plots.");
        }

        savedShortlistRevision = ReferenceEquals(shortlist, loaded) || shortlist.Count == 0 ? shortlist.Revision : -1;
    }

    private void SaveShortlistIfChanged()
    {
        if (!shortlistLoaded || shortlist.Revision == savedShortlistRevision)
        {
            return;
        }

        savedShortlistRevision = shortlist.Revision;
        store.Save(shortlist.Freeze(), ShortlistName);
    }

    private void LoadQuery()
    {
        var settings = configuration.Find;
        var districts = settings.Districts is { } chosen ? AreaScope.DistrictMaskOf(chosen.ToArray()) : AreaScope.AllDistrictsMask;
        Area = new AreaScope(0, districts, settings.AllWards, AreaScope.WardMaskOf(settings.Wards.ToArray()), settings.MainDivision, settings.Subdivision);
        Filter = PlotFilter.Default with
        {
            Sizes = (SizeFilter)settings.Sizes & SizeFilter.All,
            Buyer = Enum.IsDefined((BuyerType)settings.Buyer) ? (BuyerType)settings.Buyer : BuyerType.Unknown,
            PriceCeiling = Math.Max(0, settings.PriceCeiling),
            Statuses = (StatusFilter)settings.Statuses,
            FirstPlot = Math.Clamp(settings.FirstPlot, PlotFilter.FirstPlotNumber, PlotFilter.LastPlotNumber),
            LastPlot = Math.Clamp(settings.LastPlot, PlotFilter.FirstPlotNumber, PlotFilter.LastPlotNumber),
            MinimumEntries = settings.MinimumEntries is >= 0 ? settings.MinimumEntries : null,
            MaximumEntries = settings.MaximumEntries is >= 0 ? settings.MaximumEntries : null,
            MaximumAgeHours = Math.Max(0, settings.MaximumAgeHours),
        };
        Sort = Enum.IsDefined((ResultSort)settings.Sort) ? (ResultSort)settings.Sort : ResultSort.PreferredAreas;
        PreferredDistricts = AreaScope.DistrictMaskOf(settings.PreferredDistricts.ToArray());
    }

    private void Changed()
    {
        QueryRevision++;
        configuration.SaveDebounced();
    }

    private readonly record struct ViewInputs(int PlotsRevision, int QueryRevision, int ShortlistRevision, ulong ContentId, uint WorldId, long GilReserve, long Minute);

    private sealed record ViewRequest(
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
