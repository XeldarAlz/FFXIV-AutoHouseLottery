using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Persistence.Model;

internal readonly struct ScheduleJson
{
    public GameRegion? Region { get; init; }

    public DateTime? AnchorEntryStartUtc { get; init; }

    public int? AnchorCycleNumber { get; init; }

    public int? Revision { get; init; }

    public static ScheduleJson From(in LotterySchedule schedule) => new()
    {
        Region = schedule.Region,
        AnchorEntryStartUtc = schedule.AnchorEntryStartUtc,
        AnchorCycleNumber = schedule.AnchorCycleNumber,
        Revision = schedule.Revision,
    };

    public bool TryToSchedule(out LotterySchedule schedule)
    {
        schedule = default;
        if (Region is not { } region
            || region == GameRegion.Unknown
            || AnchorEntryStartUtc is not { Kind: DateTimeKind.Utc } anchor
            || AnchorCycleNumber is not { } number
            || Revision is not { } revision
            || revision < 1)
        {
            return false;
        }

        schedule = new LotterySchedule(region, anchor, number, revision);
        return true;
    }
}
