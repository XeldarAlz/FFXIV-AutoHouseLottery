namespace AutoHouseLottery.Core.Housing.Model;

internal readonly record struct CyclePosition(
    CycleKey Cycle,
    LotteryPhase Phase,
    DateTime PhaseStartUtc,
    DateTime PhaseEndUtc,
    int ScheduleRevision,
    CycleEvidence Evidence)
{
    public bool IsVerified => Evidence == CycleEvidence.ServerObserved;
}
