using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Housing.Model;

internal sealed record PlotSnapshot(PlotKey Key)
{
    public Observed<PlotOccupancy>? Occupancy { get; init; }

    public Observed<SaleState>? Sale { get; init; }

    public Observed<PlotSize>? Size { get; init; }

    public Observed<long>? Price { get; init; }

    public Observed<PlotBuyers>? Buyers { get; init; }

    public Observed<int>? EntryCount { get; init; }

    public ImmutableArray<FieldConflict> Conflicts { get; init; } = [];

    public bool NeedsVerification => Conflicts.Length > 0;

    public int? EntryCountIn(CycleKey cycle)
    {
        if (EntryCount is not { } count || count.Provenance.Cycle != cycle)
        {
            return null;
        }

        return count.Value;
    }

    public PlotAvailability AvailabilityAt(in CyclePosition position)
    {
        if (HasAvailabilityConflict())
        {
            return PlotAvailability.Unknown;
        }

        if (Occupancy is { Value: PlotOccupancy.Occupied } occupied
            && (Sale is not { } laterSale || !PlotMerge.Supersedes(laterSale.Provenance, occupied.Provenance)))
        {
            return PlotAvailability.Occupied;
        }

        if (Sale is not { } sale)
        {
            return PlotAvailability.Unknown;
        }

        if (sale.Value.Sale == PlotSale.NotReported
            && Occupancy is { Value: PlotOccupancy.Vacant } vacant
            && PlotMerge.Supersedes(vacant.Provenance, sale.Provenance))
        {
            return PlotAvailability.Unknown;
        }

        var current = sale.Provenance.Cycle == position.Cycle;
        return sale.Value.Sale switch
        {
            PlotSale.NotReported => PlotAvailability.NotReported,
            PlotSale.FirstComeOpen => PlotAvailability.FirstComeOpen,
            PlotSale.Unavailable when current => PlotAvailability.VacantUnavailable,
            PlotSale.LotteryEntry when current && position.Phase == LotteryPhase.Entry => PlotAvailability.LotteryEntry,
            PlotSale.LotteryResults when current && position.Phase == LotteryPhase.Results => PlotAvailability.LotteryResults,
            _ => PlotAvailability.Unknown,
        };
    }

    private bool HasAvailabilityConflict()
    {
        for (var index = 0; index < Conflicts.Length; index++)
        {
            if (Conflicts[index].Field is PlotField.Occupancy or PlotField.Sale or PlotField.Availability)
            {
                return true;
            }
        }

        return false;
    }
}
