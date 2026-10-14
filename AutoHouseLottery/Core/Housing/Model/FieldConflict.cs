namespace AutoHouseLottery.Core.Housing.Model;

internal readonly record struct FieldConflict(PlotField Field, byte KeptValue, Provenance Kept, byte OtherValue, Provenance Other);
