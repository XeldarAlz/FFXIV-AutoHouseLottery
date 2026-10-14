using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Housing.Model;

internal sealed class LotterySchedules
{
    private readonly Dictionary<GameRegion, LotterySchedule> schedules = [];

    private LotterySchedules()
    {
    }

    public ImmutableArray<LotterySchedule> All { get; private set; } = [];

    public static LotterySchedules Create(ReadOnlySpan<LotterySchedule> persisted)
    {
        var created = new LotterySchedules();
        for (var index = 0; index < LotteryCalendar.Defaults.Length; index++)
        {
            created.schedules[LotteryCalendar.Defaults[index].Region] = LotteryCalendar.Defaults[index];
        }

        for (var index = 0; index < persisted.Length; index++)
        {
            created.schedules[persisted[index].Region] = persisted[index];
        }

        created.Publish();
        return created;
    }

    public bool TryGet(GameRegion region, out LotterySchedule schedule) => schedules.TryGetValue(region, out schedule);

    public ScheduleUpdate Observe(GameRegion region, LotteryPhase phase, DateTime observedPhaseEndUtc, out CyclePosition position)
    {
        position = default;
        if (!schedules.TryGetValue(region, out var schedule))
        {
            if (!LotteryCalendar.TryEstablish(region, phase, observedPhaseEndUtc, out var established)
                || !LotteryCalendar.TryVerify(established, phase, observedPhaseEndUtc, out position))
            {
                return ScheduleUpdate.Rejected;
            }

            schedules[region] = established;
            Publish();
            return ScheduleUpdate.Established;
        }

        if (!LotteryCalendar.TryVerify(schedule, phase, observedPhaseEndUtc, out position))
        {
            return ScheduleUpdate.Rejected;
        }

        var corrected = LotteryCalendar.Correct(schedule, position);
        if (corrected.AnchorEntryStartUtc == LotteryCalendar.EntryStartOf(schedule, corrected.AnchorCycleNumber))
        {
            return ScheduleUpdate.Unchanged;
        }

        schedules[region] = corrected;
        position = position with { ScheduleRevision = corrected.Revision };
        Publish();
        return ScheduleUpdate.Reanchored;
    }

    private void Publish() => All = [.. schedules.Values];
}
