using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Find.Model;

internal readonly record struct ShortlistRow(PlotKey Key, PlotStatus? Status, PlotSize? Size, long? Price, int? Entries);
