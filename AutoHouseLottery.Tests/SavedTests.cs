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
    public void ANewMatchIsAnnouncedOnceAndOnlyAfterTheSearchWasSaved()
    {
        var library = new SavedLibrary();
        var ledger = new NoticeLedger();
        var search = library.AddSearch("Mist", Scope(HousingDistricts.Mist), PlotFilter.Default, now);
        var existing = Plots(Listing(Plot(1, 1)));
        Record(NoticeRules.DueForSearch(search, existing, VerifiedEntryPhase(), now, ledger), ledger);
        var grown = Plots(Listing(Plot(1, 1)), Listing(Plot(2, 2)));

        var first = NoticeRules.Due(library.Snapshot(), grown, VerifiedEntryPhase(), now, ledger);
        Record(first, ledger);
        var again = NoticeRules.Due(library.Snapshot(), grown, VerifiedEntryPhase(), now, ledger);

        var notice = Assert.Single(first);
        Assert.Equal((NoticeKind.NewMatch, Plot(2, 2)), (notice.Kind, notice.Plot));
        Assert.Empty(again);
    }

    [Fact]
    public void AnnouncedNoticesStayAnnouncedAfterAReload()
    {
        var library = new SavedLibrary();
        var ledger = new NoticeLedger();
        library.AddSearch("Mist", Scope(HousingDistricts.Mist), PlotFilter.Default, now);
        var plots = Plots(Listing(Plot(1, 1)));
        Record(NoticeRules.Due(library.Snapshot(), plots, VerifiedEntryPhase(), now, ledger), ledger);

        var json = SavedCodec.Export(library.Snapshot(), ledger.Entries, []);
        Assert.Equal(SavedReadError.None, SavedCodec.TryRead(json, out var document));
        var reloaded = new SavedLibrary();
        reloaded.Restore(document!.Searches.AsSpan(), document.Lists.AsSpan());
        var restoredLedger = new NoticeLedger();
        foreach (var entry in document.Delivered)
        {
            restoredLedger.Restore(entry.Key, entry.Value);
        }

        Assert.Empty(NoticeRules.Due(reloaded.Snapshot(), plots, VerifiedEntryPhase(), now, restoredLedger));
    }

    [Fact]
    public void ASavedPlotAnnouncesItsOpeningItsClosingAndItsResultsOnce()
    {
        var library = new SavedLibrary();
        var ledger = new NoticeLedger();
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
    }

    [Fact]
    public void TheSamePlotIsAnnouncedAgainInTheNextLottery()
    {
        var library = new SavedLibrary();
        var ledger = new NoticeLedger();
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
        var json = SavedCodec.Export(library.Snapshot(), new Dictionary<string, DateTime>(), [new NoticeRecord(NoticeKind.NewMatch, Plot(1, 1), "Search", now, null)]);
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
    public void AnUnreadableOrFutureFileIsReportedNotEmptied()
    {
        Assert.Equal(SavedReadError.Malformed, SavedCodec.TryRead("{not json", out _));
        Assert.Equal(SavedReadError.UnsupportedSchema, SavedCodec.TryRead("""{"schema":99}""", out _));
    }

    private static void Record(IReadOnlyList<Notice> notices, NoticeLedger ledger)
    {
        for (var index = 0; index < notices.Count; index++)
        {
            ledger.TryRecord(notices[index], now);
        }
    }
}
