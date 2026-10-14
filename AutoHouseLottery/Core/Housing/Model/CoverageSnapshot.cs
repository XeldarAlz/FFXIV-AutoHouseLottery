namespace AutoHouseLottery.Core.Housing.Model;

internal readonly record struct CoverageSnapshot(
    CoverageScope Scope,
    ObservationSource Source,
    CoverageOutcome Outcome,
    int? ExpectedRows,
    int ObservedRows,
    DateTime ObservedFromUtc,
    DateTime ObservedUntilUtc,
    DateTime ReceivedUtc);
