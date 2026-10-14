namespace AutoHouseLottery.Core;

internal static class HouseLotteryConstants
{
    public const string PrimaryCommand = "/autohouselottery";
    public const string AliasCommand = "/ahl";

    public const string LogPrefix = "[AHL]";

    public const int SaveThrottleMs = 500;

    internal static class ThrottleKeys
    {
        public const string Save = "AutoHouseLottery.Save";
    }
}
