namespace AutoHouseLottery.Core.Localization;

internal static partial class L
{
    internal static class Dev
    {
        public static readonly LocString Usage = new("dev.usage", "Usage: /ahl dev [run <district> <ward> <plot> <deposit> [fc] | settle <receipt or district ward plot> [claim]]");
        public static readonly LocString RunStarted = new("dev.runStarted", "REAL ENTRY run started for {0} at {1}. Watch it; /ahl stop stops it before Confirm.");
        public static readonly LocString RunRefused = new("dev.runRefused", "The run did not start: {0}");
        public static readonly LocString SettleStarted = new("dev.settleStarted", "{0} started for {1}; it accepts the result. Watch it; a press already made is verified, never undone.");
        public static readonly LocString SettleRefused = new("dev.settleRefused", "The visit did not start: {0}");
        public static readonly LocString SettleNotFound = new("dev.settleNotFound", "No unsettled entry of the logged-in character matches that receipt or address.");
    }
}
