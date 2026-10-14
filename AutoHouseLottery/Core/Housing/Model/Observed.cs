namespace AutoHouseLottery.Core.Housing.Model;

internal readonly record struct Observed<T>(T Value, Provenance Provenance) where T : struct;
