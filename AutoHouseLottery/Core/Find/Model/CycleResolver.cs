using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Find.Model;

internal static class CycleResolver
{
    // A phase reported by the listings counts as verified only while the time falls inside it; otherwise the calendar
    // estimate stands, and the run preview says the cycle is not yet confirmed.
    public static CyclePosition Current(in LotterySchedule schedule, ReadOnlySpan<CyclePosition> observed, DateTime nowUtc)
    {
        for (var index = 0; index < observed.Length; index++)
        {
            var position = observed[index];
            if (position.IsVerified && position.Cycle.Region == schedule.Region && position.PhaseStartUtc <= nowUtc && nowUtc < position.PhaseEndUtc)
            {
                return position;
            }
        }

        return LotteryCalendar.Estimate(schedule, nowUtc);
    }
}
