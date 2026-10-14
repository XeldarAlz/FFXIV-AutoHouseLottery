using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Selection.Model;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests;

public sealed class BestMatchRankingTests
{
    [Fact]
    public void FewestEntriesThenLowestDepositThenAddress()
    {
        var plots = Store(
            ListedPlot(Plot(1, 1), 3, 2_000_000),
            ListedPlot(Plot(1, 2), 1, 4_000_000),
            ListedPlot(Plot(1, 3), 1, 3_000_000),
            ListedPlot(Plot(1, 4), 1, 3_000_000));

        var ranking = BestMatchRanking.Rank([Plot(1, 1), Plot(1, 2), Plot(1, 3), Plot(1, 4)], plots, FirstCycle);

        Assert.Equal([Plot(1, 3), Plot(1, 4), Plot(1, 2), Plot(1, 1)], Keys(ranking));
    }

    [Fact]
    public void UnknownCountRanksAfterEveryKnownCountIncludingZero()
    {
        var plots = Store(
            ListedPlot(Plot(1, 1), null, 1_000_000),
            ListedPlot(Plot(1, 2), 0, 9_000_000),
            ListedPlot(Plot(1, 3), 7, 9_000_000));

        var ranking = BestMatchRanking.Rank([Plot(1, 1), Plot(1, 2), Plot(1, 3)], plots, FirstCycle);

        Assert.Equal([Plot(1, 2), Plot(1, 3), Plot(1, 1)], Keys(ranking));
        Assert.Null(ranking[2].ReportedEntries);
    }

    [Fact]
    public void CountsFromAnotherCycleRankAsUnknown()
    {
        var plots = Store(ListedPlot(Plot(1, 1), 0), ListedPlot(Plot(1, 2), 9));

        var ranking = BestMatchRanking.Rank([Plot(1, 1), Plot(1, 2)], plots, SecondCycle);

        Assert.Null(ranking[0].ReportedEntries);
        Assert.Null(ranking[1].ReportedEntries);
    }

    [Fact]
    public void PlotsWithoutAnyObservationStillRankLast()
    {
        var plots = Store(ListedPlot(Plot(1, 2), 5));

        var ranking = BestMatchRanking.Rank([Plot(1, 1), Plot(1, 2)], plots, FirstCycle);

        Assert.Equal([Plot(1, 2), Plot(1, 1)], Keys(ranking));
        Assert.Null(ranking[1].Deposit);
    }

    [Fact]
    public void ListedPlotOutranksAnUnreportedPlotWithFewerEntries()
    {
        var unreported = ListedPlot(Plot(1, 1), 0) with
        {
            Sale = new Observed<SaleState>(new SaleState(PlotSale.NotReported, null), Seen(ObservationSource.PublicFeed, ObservedAt.AddHours(1))),
        };
        var plots = Store(unreported, ListedPlot(Plot(1, 2), 3));

        var ranking = BestMatchRanking.Rank([Plot(1, 1), Plot(1, 2)], plots, VerifiedEntryPhase());

        Assert.Equal([Plot(1, 2), Plot(1, 1)], Keys(ranking));
        Assert.Equal(PlotAvailability.NotReported, ranking[1].Availability);
    }

    private static Dictionary<PlotKey, PlotSnapshot> Store(params PlotSnapshot[] snapshots)
    {
        var plots = new Dictionary<PlotKey, PlotSnapshot>();
        for (var index = 0; index < snapshots.Length; index++)
        {
            plots[snapshots[index].Key] = snapshots[index];
        }

        return plots;
    }

    private static PlotKey[] Keys(System.Collections.Immutable.ImmutableArray<RankedPlot> ranking)
    {
        var keys = new PlotKey[ranking.Length];
        for (var index = 0; index < keys.Length; index++)
        {
            keys[index] = ranking[index].Key;
        }

        return keys;
    }
}
