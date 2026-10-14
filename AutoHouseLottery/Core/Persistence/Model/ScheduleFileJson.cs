namespace AutoHouseLottery.Core.Persistence.Model;

internal readonly struct ScheduleFileJson
{
    public int? Schema { get; init; }

    public ScheduleJson[]? Schedules { get; init; }
}
