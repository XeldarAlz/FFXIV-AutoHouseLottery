namespace AutoHouseLottery.Core.Selection.Model;

internal static class LotteryOdds
{
    public static bool TryEstimate(ReadOnlySpan<PlotDraw> draws, out OddsEstimate estimate)
    {
        estimate = default;
        var noWin = 1d;
        var exactlyOneWin = 0d;
        for (var index = 0; index < draws.Length; index++)
        {
            var draw = draws[index];
            if (draw.ReportedEntries is not { } reported || draw.OwnEntriesInReport < 0 || draw.RunEntries < 0)
            {
                return false;
            }

            var otherEntries = reported - draw.OwnEntriesInReport;
            if (otherEntries < 0)
            {
                return false;
            }

            if (draw.RunEntries == 0)
            {
                continue;
            }

            var chance = (double)draw.RunEntries / (otherEntries + draw.RunEntries);
            exactlyOneWin = exactlyOneWin * (1d - chance) + noWin * chance;
            noWin *= 1d - chance;
        }

        estimate = new OddsEstimate(1d - noWin, Math.Max(0d, 1d - noWin - exactlyOneWin));
        return true;
    }
}
