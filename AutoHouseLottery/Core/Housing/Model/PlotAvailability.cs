namespace AutoHouseLottery.Core.Housing.Model;

internal enum PlotAvailability : byte
{
    Unknown,
    NotReported,
    VacantUnavailable,
    LotteryEntry,
    LotteryResults,
    FirstComeOpen,
    Occupied,
}
