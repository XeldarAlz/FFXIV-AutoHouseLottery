namespace AutoHouseLottery.Core.Housing.Model;

internal readonly record struct LotterySchedule(GameRegion Region, DateTime AnchorEntryStartUtc, int AnchorCycleNumber, int Revision);
