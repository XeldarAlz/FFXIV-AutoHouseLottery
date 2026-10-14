using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Find.Model;

internal readonly record struct FindQuery(AreaScope Area, PlotFilter Filter, ResultSort Sort, uint PreferredDistricts, CyclePosition Position, DateTime NowUtc);
