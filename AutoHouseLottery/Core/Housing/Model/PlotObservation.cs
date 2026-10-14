namespace AutoHouseLottery.Core.Housing.Model;

internal readonly record struct PlotObservation(PlotKey Key, Provenance Provenance)
{
    public PlotOccupancy? Occupancy { get; init; }

    public SaleState? Sale { get; init; }

    public PlotSize? Size { get; init; }

    public long? Price { get; init; }

    public PlotBuyers? Buyers { get; init; }

    public int? EntryCount { get; init; }
}
