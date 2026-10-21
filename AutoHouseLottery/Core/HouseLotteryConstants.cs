namespace AutoHouseLottery.Core;

internal static class HouseLotteryConstants
{
    public const string LogPrefix = "[AHL]";

    public const int SaveThrottleMs = 500;

    // Some plugin commands misbehave when several arrive in the same frame, so post-run commands are spaced out.
    public const int PostRunCommandSpacingMs = 500;

    internal static class ThrottleKeys
    {
        public const string Save = "AutoHouseLottery.Save";
    }
}
