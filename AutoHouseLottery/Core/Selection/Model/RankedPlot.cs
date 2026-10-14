using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Selection.Model;

internal readonly record struct RankedPlot(PlotKey Key, int? ReportedEntries, long? Deposit);
