using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Settlement.Model;

// When a cycle's results open and close, and when its refunds lapse. The live position speaks for its own cycle; other
// cycles come from the calendar, and nothing is known for a cycle the calendar does not cover.
internal static class SettlementDeadlines
{
    // The housing guide gives losing deposits a 90-day refund window. It is counted from the start of the results
    // period, the earliest the game could start it, so a deadline notice is never late.
    public const int RefundWindowDays = 90;

    public const int ShownDeadlineToleranceDays = 3;

    private static readonly TimeSpan refundWindow = TimeSpan.FromDays(RefundWindowDays);
    private static readonly TimeSpan shownDeadlineTolerance = TimeSpan.FromDays(ShownDeadlineToleranceDays);

    public static bool ResultsArrived(CycleKey entered, in CyclePosition current)
        => entered.Region == current.Cycle.Region
        && (current.Cycle.IsLaterThan(entered) || (current.Cycle == entered && current.Phase == LotteryPhase.Results));

    public static DateTime? ResultsStart(CycleKey cycle, in CyclePosition current, LotterySchedule? schedule)
    {
        if (cycle == current.Cycle)
        {
            return current.Phase == LotteryPhase.Results ? current.PhaseStartUtc : current.PhaseEndUtc;
        }

        return EntryStart(cycle, schedule) is { } entryStart ? entryStart + LotteryCalendar.EntryPeriod : null;
    }

    // The last moment a win can be claimed.
    public static DateTime? ResultsEnd(CycleKey cycle, in CyclePosition current, LotterySchedule? schedule)
    {
        if (cycle == current.Cycle)
        {
            return current.Phase == LotteryPhase.Results ? current.PhaseEndUtc : current.PhaseEndUtc + LotteryCalendar.ResultsPeriod;
        }

        return EntryStart(cycle, schedule) is { } entryStart ? entryStart + LotteryCalendar.CycleLength : null;
    }

    public static DateTime? RefundDeadline(CycleKey cycle, in CyclePosition current, LotterySchedule? schedule)
        => ResultsStart(cycle, current, schedule) is { } start ? start + refundWindow : null;

    // The deadline a dialog showed is trusted only within a few days of the estimate: a date read in the wrong order
    // (day and month swapped) or the wrong time zone lands far from it. Without an estimate nothing can vouch for it.
    public static bool PlausibleShownDeadline(DateTime shownUtc, DateTime? estimateUtc)
        => estimateUtc is { } estimate && (shownUtc - estimate).Duration() <= shownDeadlineTolerance;

    private static DateTime? EntryStart(CycleKey cycle, LotterySchedule? schedule)
        => schedule is { } calendar && calendar.Region == cycle.Region ? LotteryCalendar.EntryStartOf(calendar, cycle.Number) : null;
}
