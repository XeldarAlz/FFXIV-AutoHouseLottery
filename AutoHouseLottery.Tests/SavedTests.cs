using System.Collections.Frozen;
using System.Linq;
using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Saved.Model;
using static AutoHouseLottery.Tests.FindFixtures;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests;

public sealed class SavedTests
{
    private static readonly DateTime now = ObservedAt.AddHours(1);
    private static readonly FrozenSet<uint> homeLoaded = new[] { HomeWorld }.ToFrozenSet();
    private static readonly FrozenSet<uint> nothingLoaded = FrozenSet<uint>.Empty;

    [Fact]
    public void ASearchFindsNewPlotsWhileAListKeepsItsOwn()
    {
        var library = new SavedLibrary();
        var search = library.AddSearch("Small in Mist", Scope(HousingDistricts.Mist), PlotFilter.Default, now);
        var list = library.AddList("Favourites", [Plot(1, 1), Plot(1, 2)], now);
        var before = Plots(Listing(Plot(1, 1)), Listing(Plot(1, 2)));
        var after = Plots(Listing(Plot(1, 1)), Listing(Plot(1, 2), PlotSale.NotReported), Listing(Plot(3, 3)));

        Assert.Equal([Plot(1, 1), Plot(1, 2)], search.Matches(before, VerifiedEntryPhase(), now).ToArray());
        Assert.Equal([Plot(1, 1), Plot(3, 3)], search.Matches(after, VerifiedEntryPhase(), now).ToArray());
        Assert.Equal([Plot(1, 1), Plot(1, 2)], list.Plots.ToArray());
    }

    [Fact]
    public void AListDeduplicatesItsPlotsAndKnowsWhichWorldsToWatch()
    {
        var library = new SavedLibrary();
        library.AddList("Twice", [Plot(1, 1), Plot(1, 1), Plot(2, 2, OtherWorld)], now);
        library.AddSearch("Home", Scope(HousingDistricts.Mist), PlotFilter.Default, now);
        var worlds = new List<uint>();

        library.CollectWorlds(worlds);

        Assert.Equal(2, library.Lists[0].Plots.Length);
        Assert.Equal([HomeWorld, OtherWorld], worlds.Order().ToArray());
        Assert.True(library.IsSaved(Plot(1, 1)));
    }

    [Fact]
    public void ANewSearchAnnouncesOnlyPlotsThatAppearAfterItWasSaved()
    {
        var (library, ledger, baseline) = Empty();
        var search = library.AddSearch("Mist", Scope(HousingDistricts.Mist), PlotFilter.Default, now);
        baseline.AddSource(search.Id);
        var existing = Plots(Listing(Plot(1, 1)));

        var waiting = Plan(library, existing, ledger, baseline, nothingLoaded);
        var settled = Plan(library, existing, ledger, baseline, homeLoaded);
        var grown = Plan(library, Plots(Listing(Plot(1, 1)), Listing(Plot(2, 2))), ledger, baseline, homeLoaded);

        Assert.Empty(waiting.Announce);
        Assert.Empty(waiting.Silent);
        Assert.Empty(settled.Announce);
        Assert.Equal([Plot(1, 1)], settled.Silent.Select(notice => notice.Plot).ToArray());
        Assert.Equal([search.Id], settled.ReadySources.ToArray());
        var notice = Assert.Single(grown.Announce);
        Assert.Equal((NoticeKind.NewMatch, Plot(2, 2)), (notice.Kind, notice.Plot));
    }

    [Fact]
    public void APlotAddedToAListIsSettledAloneAndOtherPlotsStillAnnounce()
    {
        var (library, ledger, baseline) = Empty();
        var list = library.AddList("A", [Plot(1, 1)], now);
        library.AddPlot(list.Id, Plot(1, 2));
        baseline.AddPlot(Plot(1, 2));

        var plan = Plan(library, Plots(Listing(Plot(1, 1)), Listing(Plot(1, 2))), ledger, baseline, homeLoaded);

        Assert.Equal([Plot(1, 1)], plan.Announce.Select(notice => notice.Plot).ToArray());
        Assert.Equal([Plot(1, 2)], plan.Silent.Select(notice => notice.Plot).ToArray());
        Assert.Equal([Plot(1, 2)], plan.ReadyPlots.ToArray());
    }

    [Fact]
    public void AnnouncedNoticesAndPendingBaselinesSurviveAReload()
    {
        var (library, ledger, baseline) = Empty();
        library.AddSearch("Mist", Scope(HousingDistricts.Mist), PlotFilter.Default, now);
        var pendingSearch = library.AddSearch("Goblet", Scope(HousingDistricts.Goblet), PlotFilter.Default, now);
        baseline.AddSource(pendingSearch.Id);
        baseline.AddPlot(Plot(4, 4, OtherWorld));
        var plots = Plots(Listing(Plot(1, 1)));
        Record(Plan(library, plots, ledger, baseline, nothingLoaded).Announce, ledger);

        var json = SavedCodec.Export(library.Snapshot(), ledger.Entries, [], baseline);
        Assert.Equal(SavedReadError.None, SavedCodec.TryRead(json, out var document));
        var reloaded = new SavedLibrary();
        reloaded.MergeLoaded(document!.Searches.AsSpan(), document.Lists.AsSpan());
        var restoredLedger = new NoticeLedger();
        foreach (var entry in document.Delivered)
        {
            restoredLedger.Restore(entry.Key, entry.Value);
        }

        Assert.Empty(Plan(reloaded, plots, restoredLedger, new NoticeBaseline(), nothingLoaded).Announce);
        Assert.Equal([pendingSearch.Id], document.PendingSources.ToArray());
        Assert.Equal([Plot(4, 4, OtherWorld)], document.PendingPlots.ToArray());
    }

    [Fact]
    public void ASearchSavedBeforeTheFileLoadedIsKeptAfterIt()
    {
        var onDisk = new SavedLibrary();
        onDisk.AddList("Disk", [Plot(1, 1)], now);
        var json = SavedCodec.Export(onDisk.Snapshot(), new Dictionary<string, DateTime>(), [], new NoticeBaseline());
        var live = new SavedLibrary();
        var early = live.AddSearch("Early", Scope(HousingDistricts.Mist), PlotFilter.Default, now);

        Assert.Equal(SavedReadError.None, SavedCodec.TryRead(json, out var document));
        live.MergeLoaded(document!.Searches.AsSpan(), document.Lists.AsSpan());

        Assert.Equal([early.Id], live.Searches.Select(search => search.Id).ToArray());
        Assert.Equal(["Disk"], live.Lists.Select(list => list.Name).ToArray());
    }

    [Fact]
    public void ASavedPlotAnnouncesItsOpeningItsClosingAndItsResultsOnce()
    {
        var (library, ledger, baseline) = Empty();
        library.AddList("A", [Plot(1, 1)], now);
        library.AddList("B", [Plot(1, 1)], now);
        var position = VerifiedEntryPhase();
        var open = Plots(Listing(Plot(1, 1)));

        var opened = NoticeRules.Due(library.Snapshot(), open, position, now, ledger);
        Record(opened, ledger);
        var closing = NoticeRules.Due(library.Snapshot(), open, position, position.PhaseEndUtc.AddMinutes(-10), ledger);
        Record(closing, ledger);
        var resultsPosition = LotteryCalendar.Estimate(LotteryCalendar.Global, position.PhaseEndUtc.AddHours(1));
        var results = NoticeRules.Due(library.Snapshot(), Plots(Listing(Plot(1, 1), PlotSale.LotteryResults)), resultsPosition, position.PhaseEndUtc.AddHours(1), ledger);

        Assert.Equal(NoticeKind.EntryOpened, Assert.Single(opened).Kind);
        Assert.Equal(NoticeKind.EntryClosing, Assert.Single(closing).Kind);
        Assert.Equal(NoticeKind.ResultsAvailable, Assert.Single(results).Kind);
        Assert.Empty(baseline.Plots);
    }

    [Fact]
    public void TheSamePlotIsAnnouncedAgainInTheNextLottery()
    {
        var (library, ledger, _) = Empty();
        library.AddList("A", [Plot(1, 1)], now);
        Record(NoticeRules.Due(library.Snapshot(), Plots(Listing(Plot(1, 1))), VerifiedEntryPhase(), now, ledger), ledger);
        var nextCycle = VerifiedEntryPhase() with { Cycle = SecondCycle };

        var next = NoticeRules.Due(library.Snapshot(), Plots(Listing(Plot(1, 1), cycle: SecondCycle)), nextCycle, now, ledger);

        Assert.Equal(SecondCycle, Assert.Single(next).Cycle);
    }

    [Fact]
    public void OldLedgerEntriesArePruned()
    {
        var ledger = new NoticeLedger();
        ledger.Restore("old", now.AddDays(-NoticeLedger.RetentionDays - 1));
        ledger.Restore("recent", now.AddDays(-1));

        Assert.Equal(1, ledger.Prune(now));
        Assert.Equal(["recent"], ledger.Entries.Keys.ToArray());
    }

    [Fact]
    public void SearchesAndListsSurviveARoundTripAndADamagedEntryIsSkippedAlone()
    {
        var library = new SavedLibrary();
        var area = Scope(HousingDistricts.Mist, HousingDistricts.Empyreum) with { AllWards = false, WardMask = AreaScope.WardMaskOf([3, 4]), Subdivision = false };
        var filter = PlotFilter.Default with { Sizes = SizeFilter.Small | SizeFilter.Medium, Buyer = BuyerType.FreeCompany, PriceCeiling = 4_000_000, MaximumEntries = 6 };
        library.AddSearch("Search", area, filter, now);
        library.AddList("List", [Plot(1, 1), Plot(2, 31, OtherWorld)], now);
        var json = SavedCodec.Export(library.Snapshot(), new Dictionary<string, DateTime>(), [new NoticeRecord(NoticeKind.NewMatch, Plot(1, 1), "Search", now, null)], new NoticeBaseline());
        var damaged = json.Replace("\"plot\": 31", "\"plot\": 99", StringComparison.Ordinal);

        Assert.Equal(SavedReadError.None, SavedCodec.TryRead(json, out var intact));
        Assert.Equal(SavedReadError.None, SavedCodec.TryRead(damaged, out var partial));

        Assert.Equal(area, intact!.Searches[0].Area);
        Assert.Equal(filter, intact.Searches[0].Filter);
        Assert.Equal(library.Lists[0].Plots.ToArray(), intact.Lists[0].Plots.ToArray());
        Assert.Single(intact.Recent);
        Assert.Single(partial!.Searches);
        Assert.Empty(partial.Lists);
        Assert.Equal(1, partial.Skipped);
    }

    [Fact]
    public void UnknownEnumValuesInASavedFileAreDroppedNotTrusted()
    {
        const string json = """
            {"schema":1,
             "searches":[{"id":"6f9619ff-8b86-d011-b42d-00cf4fc964ff","name":"Odd","world":63,"districts":[339],
                          "buyer":7,"sizes":96,"statuses":255,"priceCeiling":-5,"firstPlot":0,"lastPlot":99}],
             "recent":[{"kind":9,"plot":{"region":"Global","world":63,"district":339,"ward":1,"plot":1},"atUtc":"2026-10-14T20:00:00Z"}]}
            """;

        Assert.Equal(SavedReadError.None, SavedCodec.TryRead(json, out var document));

        var filter = document!.Searches[0].Filter;
        Assert.Equal(BuyerType.Unknown, filter.Buyer);
        Assert.Equal(SizeFilter.All, filter.Sizes);
        Assert.Equal(StatusFilter.EntryOpen | StatusFilter.Results | StatusFilter.NextCycle | StatusFilter.ForSale | StatusFilter.Occupied | StatusFilter.NotReported | StatusFilter.NeedsRefresh, filter.Statuses);
        Assert.Equal(0, filter.PriceCeiling);
        Assert.Equal((PlotFilter.FirstPlotNumber, PlotFilter.LastPlotNumber), (filter.FirstPlot, filter.LastPlot));
        Assert.Empty(document.Recent);
    }

    [Fact]
    public void AnUnreadableOrFutureFileIsReportedNotEmptied()
    {
        Assert.Equal(SavedReadError.Malformed, SavedCodec.TryRead("{not json", out _));
        Assert.Equal(SavedReadError.UnsupportedSchema, SavedCodec.TryRead("""{"schema":99}""", out _));
    }

    private static (SavedLibrary Library, NoticeLedger Ledger, NoticeBaseline Baseline) Empty() => (new SavedLibrary(), new NoticeLedger(), new NoticeBaseline());

    private static NoticePlan Plan(SavedLibrary library, Dictionary<PlotKey, PlotSnapshot> plots, NoticeLedger ledger, NoticeBaseline baseline, FrozenSet<uint> loaded)
    {
        var plan = NoticePlanner.Plan(library.Snapshot(), plots, VerifiedEntryPhase(), now, ledger, baseline, loaded);
        Record(plan.Silent, ledger);
        baseline.Settle(plan.ReadySources.AsSpan(), plan.ReadyPlots.AsSpan());
        return plan;
    }

    private static void Record(IReadOnlyList<Notice> notices, NoticeLedger ledger)
    {
        for (var index = 0; index < notices.Count; index++)
        {
            ledger.TryRecord(notices[index], now);
        }
    }
}
