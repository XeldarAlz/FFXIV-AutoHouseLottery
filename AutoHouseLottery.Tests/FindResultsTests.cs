using System.Linq;
using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Selection.Model;
using static AutoHouseLottery.Tests.FindFixtures;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests;

public sealed class FindResultsTests
{
    [Fact]
    public void AddAllMatchingCountsAndAddsExactlyThePlotsInsideItsScope()
    {
        var plots = Plots(
            Listing(Plot(1, 5)),
            Listing(Plot(10, 31)),
            Listing(Plot(11, 5)),
            Listing(Plot(4, 2, district: HousingDistricts.LavenderBeds)),
            Listing(Plot(4, 2, district: HousingDistricts.Goblet)),
            Listing(Plot(4, 3, OtherWorld)));
        var area = Scope(HousingDistricts.Mist, HousingDistricts.LavenderBeds) with { AllWards = false, WardMask = AreaScope.WardRange(1, 10) };
        var results = FindResultsBuilder.Build(plots, Query(area));
        var shortlist = new Shortlist(Guid.NewGuid(), "Shortlist");

        var added = shortlist.AddRange(results.MatchingKeys.AsSpan());

        Assert.Equal(3, results.Count);
        Assert.Equal(results.Count, added);
        Assert.All(shortlist.Plots, key => Assert.True(area.Contains(key)));
        var summary = ScopeSummary.Describe(area);
        Assert.Equal([HousingDistricts.Mist, HousingDistricts.LavenderBeds], summary.Districts.ToArray());
        Assert.Equal((WardSpan.Range, 1, 10), (summary.Wards, summary.FirstWard, summary.LastWard));
        Assert.Equal(DivisionSpan.Both, summary.Divisions);
    }

    [Fact]
    public void AddingAllMatchingTwiceNeverDuplicatesAPlot()
    {
        var results = FindResultsBuilder.Build(Plots(Listing(Plot(1, 1)), Listing(Plot(1, 2))), Query(Scope(HousingDistricts.Mist)));
        var shortlist = new Shortlist(Guid.NewGuid(), "Shortlist");

        shortlist.AddRange(results.MatchingKeys.AsSpan());
        var second = shortlist.AddRange(results.MatchingKeys.AsSpan());

        Assert.Equal(0, second);
        Assert.Equal(2, shortlist.Count);
    }

    [Fact]
    public void ChangingFiltersNeverRemovesShortlistedPlots()
    {
        var plots = Plots(Listing(Plot(1, 1), size: PlotSize.Small), Listing(Plot(1, 2), size: PlotSize.Large));
        var shortlist = new Shortlist(Guid.NewGuid(), "Shortlist");
        shortlist.AddRange(FindResultsBuilder.Build(plots, Query(Scope(HousingDistricts.Mist))).MatchingKeys.AsSpan());

        var narrowed = FindResultsBuilder.Build(plots, Query(Scope(HousingDistricts.Mist), PlotFilter.Default with { Sizes = SizeFilter.Small }));
        var elsewhere = FindResultsBuilder.Build(plots, Query(Scope(HousingDistricts.Goblet)));

        Assert.Equal(1, narrowed.Count);
        Assert.Equal(0, elsewhere.Count);
        Assert.Equal(2, shortlist.Count);
    }

    [Fact]
    public void UnknownValuesNeverPassAFilterThatNarrowsOnThem()
    {
        var plots = Plots(
            Listing(Plot(1, 1), price: null),
            Listing(Plot(1, 2), size: null),
            Listing(Plot(1, 3), entries: null),
            Listing(Plot(1, 4)));
        var area = Scope(HousingDistricts.Mist);

        var priced = FindResultsBuilder.Build(plots, Query(area, PlotFilter.Default with { PriceCeiling = 4_000_000 }));
        var sized = FindResultsBuilder.Build(plots, Query(area, PlotFilter.Default with { Sizes = SizeFilter.Small }));
        var counted = FindResultsBuilder.Build(plots, Query(area, PlotFilter.Default with { MaximumEntries = 5 }));
        var everything = FindResultsBuilder.Build(plots, Query(area));

        Assert.DoesNotContain(Plot(1, 1), priced.MatchingKeys);
        Assert.DoesNotContain(Plot(1, 2), sized.MatchingKeys);
        Assert.DoesNotContain(Plot(1, 3), counted.MatchingKeys);
        Assert.Equal(4, everything.Count);
    }

    [Fact]
    public void ACeilingIsInclusiveAndBuyerTypeFollowsThePlotsAllowedBuyers()
    {
        var plots = Plots(
            Listing(Plot(1, 1), price: 4_000_000),
            Listing(Plot(1, 2), price: 4_000_001),
            Listing(Plot(1, 3), buyers: PlotBuyers.FreeCompany));
        var filter = PlotFilter.Default with { PriceCeiling = 4_000_000, Buyer = BuyerType.Private };

        var results = FindResultsBuilder.Build(plots, Query(Scope(HousingDistricts.Mist), filter));

        Assert.Equal([Plot(1, 1)], results.MatchingKeys.ToArray());
    }

    [Fact]
    public void FewestEntriesSortsUnknownCountsLastAndTiesByAddress()
    {
        var plots = Plots(
            Listing(Plot(2, 1), entries: null),
            Listing(Plot(1, 9), entries: 4),
            Listing(Plot(1, 2), entries: 0),
            Listing(Plot(1, 1), entries: 4));

        var results = FindResultsBuilder.Build(plots, Query(Scope(HousingDistricts.Mist), sort: ResultSort.FewestEntries));

        Assert.Equal([Plot(1, 2), Plot(1, 1), Plot(1, 9), Plot(2, 1)], results.MatchingKeys.ToArray());
    }

    [Fact]
    public void PreferredAreasComeFirstThenTheAddress()
    {
        var plots = Plots(Listing(Plot(1, 1)), Listing(Plot(1, 1, district: HousingDistricts.Empyreum)));
        var query = Query(Scope(HousingDistricts.Mist, HousingDistricts.Empyreum), sort: ResultSort.PreferredAreas) with
        {
            PreferredDistricts = AreaScope.DistrictBit(HousingDistricts.Empyreum),
        };

        var results = FindResultsBuilder.Build(plots, query);

        Assert.Equal(HousingDistricts.Empyreum, results.MatchingKeys[0].DistrictId);
    }

    [Fact]
    public void EachLotteryStateKeepsItsOwnStatus()
    {
        var position = VerifiedEntryPhase();
        var conflicted = Listing(Plot(1, 7)) with
        {
            Conflicts = [new FieldConflict(PlotField.Availability, 0, Seen(ObservationSource.WardScan, ObservedAt), 1, Seen(ObservationSource.PublicFeed, ObservedAt))],
        };

        Assert.Equal(PlotStatus.EntryOpen, PlotStatuses.Classify(Listing(Plot(1, 1)), position));
        Assert.Equal(PlotStatus.NextCycle, PlotStatuses.Classify(Listing(Plot(1, 2), PlotSale.Unavailable), position));
        Assert.Equal(PlotStatus.ForSale, PlotStatuses.Classify(Listing(Plot(1, 3), PlotSale.FirstComeOpen), position));
        Assert.Equal(PlotStatus.NotReported, PlotStatuses.Classify(Listing(Plot(1, 4), PlotSale.NotReported), position));
        Assert.Equal(PlotStatus.NeedsRefresh, PlotStatuses.Classify(Listing(Plot(1, 5), cycle: SecondCycle), position));
        Assert.Equal(PlotStatus.NeedsRefresh, PlotStatuses.Classify(Listing(Plot(1, 6), PlotSale.LotteryResults), position));
        Assert.Equal(PlotStatus.NeedsRefresh, PlotStatuses.Classify(conflicted, position));
        Assert.Equal(PlotStatus.NeedsRefresh, PlotStatuses.Classify(Listing(Plot(1, 8)) with { Sale = null }, position));
        Assert.Equal(PlotStatus.NotReported, PlotStatuses.Classify(new PlotSnapshot(Plot(1, 9)), position));
    }

    [Fact]
    public void ReportedVacantAndEntryOpenCountTheWholeScopeNotJustTheMatches()
    {
        var plots = Plots(Listing(Plot(1, 1)), Listing(Plot(1, 2), PlotSale.Unavailable), Listing(Plot(1, 3), PlotSale.NotReported));

        var results = FindResultsBuilder.Build(plots, Query(Scope(HousingDistricts.Mist)));

        Assert.Equal(2, results.ReportedVacant);
        Assert.Equal(1, results.EntryOpen);
        Assert.Equal(1, results.Count);
        Assert.Equal(1, results.MatchesInWard(HousingDistricts.Mist, 1));
    }

    [Fact]
    public void ScopeSummaryNamesAllSingleRangeAndScatteredWards()
    {
        var mist = Scope(HousingDistricts.Mist);

        Assert.Equal(WardSpan.All, ScopeSummary.Describe(mist).Wards);
        Assert.True(ScopeSummary.Describe(Scope(HousingDistricts.All.Select(district => (uint)district.TerritoryId).ToArray())).AllDistricts);
        Assert.Equal(WardSpan.All, ScopeSummary.Describe(mist with { AllWards = false, WardMask = AreaScope.WardRange(1, 30) }).Wards);
        Assert.Equal(WardSpan.Single, ScopeSummary.Describe(mist with { AllWards = false, WardMask = AreaScope.WardBit(12) }).Wards);
        var scattered = ScopeSummary.Describe(mist with { AllWards = false, WardMask = AreaScope.WardMaskOf([2, 5, 9]) });
        Assert.Equal((WardSpan.Several, 3), (scattered.Wards, scattered.WardCount));
        Assert.Equal(DivisionSpan.Subdivision, ScopeSummary.Describe(mist with { MainDivision = false }).Divisions);
    }

    [Fact]
    public void RemoveUnavailableKeepsOpenAndUnknownPlots()
    {
        var plots = Plots(Listing(Plot(1, 1)), Listing(Plot(1, 2), PlotSale.NotReported), Listing(Plot(1, 3), PlotSale.Unavailable), Listing(Plot(1, 4), cycle: SecondCycle));
        IReadOnlyList<PlotKey> shortlist = [Plot(1, 1), Plot(1, 2), Plot(1, 3), Plot(1, 4), Plot(1, 5)];

        var rows = ShortlistRules.Rows(shortlist, plots, VerifiedEntryPhase());

        Assert.Equal([Plot(1, 2), Plot(1, 3)], ShortlistRules.Unavailable(rows).ToArray());
        Assert.Null(rows[4].Status);
    }

    [Fact]
    public void AReportedPhaseVerifiesTheCycleOnlyWhileItLasts()
    {
        var verified = VerifiedEntryPhase();

        var during = CycleResolver.Current(LotteryCalendar.Global, [verified], ObservedAt);
        var after = CycleResolver.Current(LotteryCalendar.Global, [verified], verified.PhaseEndUtc.AddMinutes(1));

        Assert.True(during.IsVerified);
        Assert.False(after.IsVerified);
        Assert.Equal(LotteryPhase.Results, after.Phase);
    }
}
