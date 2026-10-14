namespace AutoHouseLottery.Core.Housing.Model;

internal readonly record struct Provenance(ObservationSource Source, DateTime SourceTimeUtc, DateTime ReceivedUtc, CycleKey? Cycle);
