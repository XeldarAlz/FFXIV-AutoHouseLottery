namespace AutoHouseLottery.Core.Localization;

internal static partial class L
{
    internal static class Dev
    {
        public static readonly LocString Usage = new("dev.usage", "Usage: /ahl dev [run <district> <ward> <plot> <deposit> [fc] | allow-real-entry <character name> <district> <ward> <plot> <max gil> | revoke-real-entry]");
        public static readonly LocString DryRunStarted = new("dev.dryRunStarted", "Dry run started for {0} at {1}. It stops at the lottery confirmation and closes it.");
        public static readonly LocString RealRunStarted = new("dev.realRunStarted", "REAL ENTRY run started for {0} at {1}. Watch it; /ahl stop stops it before Confirm.");
        public static readonly LocString RunRefused = new("dev.runRefused", "The run did not start: {0}");
        public static readonly LocString GrantWarning = new("dev.grantWarning", "REAL ENTRY ALLOWED for this session only: {0} may enter {1} once, for at most {2}. The next run on that plot deposits real gil and cannot be undone. /ahl dev revoke-real-entry withdraws it.");
        public static readonly LocString GrantRefused = new("dev.grantRefused", "Not allowed: name the logged-in character, a district, a ward, a plot on its home world and a gil ceiling above zero.");
        public static readonly LocString Revoked = new("dev.revoked", "Real entry permission withdrawn. Every run is a dry run again.");
    }
}
