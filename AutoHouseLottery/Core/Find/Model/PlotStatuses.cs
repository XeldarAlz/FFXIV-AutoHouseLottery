using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Find.Model;

internal static class PlotStatuses
{
    public static PlotStatus Classify(PlotSnapshot plot, in CyclePosition position)
    {
        if (plot.NeedsVerification)
        {
            return PlotStatus.NeedsRefresh;
        }

        return plot.AvailabilityAt(position) switch
        {
            PlotAvailability.LotteryEntry => PlotStatus.EntryOpen,
            PlotAvailability.LotteryResults => PlotStatus.Results,
            PlotAvailability.VacantUnavailable => PlotStatus.NextCycle,
            PlotAvailability.FirstComeOpen => PlotStatus.ForSale,
            PlotAvailability.Occupied => PlotStatus.Occupied,
            PlotAvailability.NotReported => PlotStatus.NotReported,
            _ => plot.Sale is null ? PlotStatus.NotReported : PlotStatus.NeedsRefresh,
        };
    }

    public static bool IsReportedVacant(PlotStatus status) => status is PlotStatus.EntryOpen or PlotStatus.Results or PlotStatus.NextCycle or PlotStatus.ForSale;

    // When the listing itself was last seen: the newest of its sale and occupancy observations.
    public static DateTime ObservedUtc(PlotSnapshot plot)
    {
        var sale = plot.Sale?.Provenance.SourceTimeUtc ?? default;
        var occupancy = plot.Occupancy?.Provenance.SourceTimeUtc ?? default;
        return sale > occupancy ? sale : occupancy;
    }

    public static ResultRow Row(PlotSnapshot plot, in CyclePosition position)
    {
        return new ResultRow(
            plot.Key,
            Classify(plot, position),
            plot.Size?.Value,
            plot.Price?.Value,
            plot.EntryCountIn(position.Cycle),
            plot.Buyers?.Value,
            ObservedUtc(plot),
            plot.Sale?.Value.PhaseEndUtc);
    }
}
