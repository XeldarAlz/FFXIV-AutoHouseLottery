using AutoHouseLottery.Core.Selection.Model;

namespace AutoHouseLottery.Tests;

public sealed class LotteryOddsTests
{
    private const int Entries = 3;

    [Theory]
    [InlineData(2, 60, 70, 26)]
    [InlineData(10, 23, 25, 2)]
    public void ThreeEntriesMatchThePublishedTable(int reportedEntries, int samePlotPercent, int spreadPercent, int twoOrMorePercent)
    {
        Assert.True(LotteryOdds.TryEstimate([new PlotDraw(reportedEntries, 0, Entries)], out var samePlot));
        Assert.True(LotteryOdds.TryEstimate(
            [new PlotDraw(reportedEntries, 0, 1), new PlotDraw(reportedEntries, 0, 1), new PlotDraw(reportedEntries, 0, 1)],
            out var spread));

        Assert.Equal(samePlotPercent, Percent(samePlot.AnyWin));
        Assert.Equal(0, Percent(samePlot.TwoOrMoreWins));
        Assert.Equal(spreadPercent, Percent(spread.AnyWin));
        Assert.Equal(twoOrMorePercent, Percent(spread.TwoOrMoreWins));
    }

    [Fact]
    public void AnyUnknownCountGivesNoEstimate()
    {
        Assert.False(LotteryOdds.TryEstimate([new PlotDraw(2, 0, 1), new PlotDraw(null, 0, 1)], out _));
    }

    [Fact]
    public void OwnConfirmedEntriesAreSubtractedBeforeCalculating()
    {
        Assert.True(LotteryOdds.TryEstimate([new PlotDraw(3, 1, 1)], out var withOwnEntry));
        Assert.True(LotteryOdds.TryEstimate([new PlotDraw(2, 0, 1)], out var othersOnly));

        Assert.Equal(othersOnly, withOwnEntry);
    }

    [Fact]
    public void MoreOwnEntriesThanReportedGivesNoEstimate()
    {
        Assert.False(LotteryOdds.TryEstimate([new PlotDraw(1, 2, 2)], out _));
    }

    private static int Percent(double chance) => (int)Math.Round(chance * 100, MidpointRounding.AwayFromZero);
}
