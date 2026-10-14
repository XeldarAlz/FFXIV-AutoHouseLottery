namespace AutoHouseLottery.Core.Housing.Model;

internal static class LotteryCalendar
{
    public const int EntryPeriodDays = 5;
    public const int ResultsPeriodDays = 4;
    public const int CycleDays = EntryPeriodDays + ResultsPeriodDays;

    private const int AnchorCycleNumber = 1;
    private const int FirstRevision = 1;
    private const int MaximumScheduleDriftDays = 2;

    public static readonly TimeSpan EntryPeriod = TimeSpan.FromDays(EntryPeriodDays);
    public static readonly TimeSpan ResultsPeriod = TimeSpan.FromDays(ResultsPeriodDays);
    public static readonly TimeSpan CycleLength = TimeSpan.FromDays(CycleDays);

    private static readonly TimeSpan maximumScheduleDrift = TimeSpan.FromDays(MaximumScheduleDriftDays);

    public static readonly LotterySchedule Global = new(
        GameRegion.Global,
        new DateTime(2026, 10, 14, 15, 0, 0, DateTimeKind.Utc),
        AnchorCycleNumber,
        FirstRevision);

    public static CyclePosition Estimate(in LotterySchedule schedule, DateTime nowUtc)
    {
        var cycleIndex = Math.DivRem((nowUtc - schedule.AnchorEntryStartUtc).Ticks, CycleLength.Ticks, out var remainder);
        if (remainder < 0)
        {
            cycleIndex--;
        }

        var cycleStart = schedule.AnchorEntryStartUtc.AddTicks(cycleIndex * CycleLength.Ticks);
        var resultsStart = cycleStart + EntryPeriod;
        var cycle = new CycleKey(schedule.Region, checked(schedule.AnchorCycleNumber + (int)cycleIndex));
        if (nowUtc < resultsStart)
        {
            return new CyclePosition(cycle, LotteryPhase.Entry, cycleStart, resultsStart, schedule.Revision, CycleEvidence.Estimated);
        }

        return new CyclePosition(cycle, LotteryPhase.Results, resultsStart, cycleStart + CycleLength, schedule.Revision, CycleEvidence.Estimated);
    }

    public static bool TryVerify(in LotterySchedule schedule, LotteryPhase phase, DateTime observedPhaseEndUtc, out CyclePosition position)
    {
        position = default;
        if (phase == LotteryPhase.Unknown || observedPhaseEndUtc == default)
        {
            return false;
        }

        var phaseLength = phase == LotteryPhase.Entry ? EntryPeriod : ResultsPeriod;
        var impliedCycleStart = observedPhaseEndUtc - (phase == LotteryPhase.Entry ? EntryPeriod : CycleLength);
        var offsetTicks = (impliedCycleStart - schedule.AnchorEntryStartUtc).Ticks;
        var cycleIndex = (long)Math.Round((double)offsetTicks / CycleLength.Ticks, MidpointRounding.AwayFromZero);
        var drift = TimeSpan.FromTicks(offsetTicks - cycleIndex * CycleLength.Ticks);
        if (drift.Duration() > maximumScheduleDrift)
        {
            return false;
        }

        var cycle = new CycleKey(schedule.Region, checked(schedule.AnchorCycleNumber + (int)cycleIndex));
        position = new CyclePosition(
            cycle,
            phase,
            observedPhaseEndUtc - phaseLength,
            observedPhaseEndUtc,
            schedule.Revision,
            CycleEvidence.ServerObserved);
        return true;
    }

    public static LotterySchedule Correct(in LotterySchedule schedule, in CyclePosition verified)
    {
        var entryStart = verified.Phase == LotteryPhase.Entry ? verified.PhaseStartUtc : verified.PhaseStartUtc - EntryPeriod;
        return new LotterySchedule(schedule.Region, entryStart, verified.Cycle.Number, schedule.Revision + 1);
    }
}
