using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Find.Model;

internal readonly record struct ResultRow(
    PlotKey Key,
    PlotStatus Status,
    PlotSize? Size,
    long? Price,
    int? Entries,
    PlotBuyers? Buyers,
    DateTime ObservedUtc,
    DateTime? PhaseEndUtc);
