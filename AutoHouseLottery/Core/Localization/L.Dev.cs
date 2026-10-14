namespace AutoHouseLottery.Core.Localization;

internal static partial class L
{
    internal static class Dev
    {
        public static readonly LocString Usage = new("dev.usage", "Usage: /ahl dev [run <district> <ward> <plot> <deposit> [fc] [real] | allow-real-entry <character name> <district> <ward> <plot> <max gil> | revoke-real-entry | settle <receipt or district ward plot> [claim] [real] | allow-real-refund <character name> <district> <ward> <plot> | allow-real-claim <character name> <district> <ward> <plot> | revoke-real-settlement | allow-switching | allow-refund-switching | revoke-switching]");
        public static readonly LocString DryRunStarted = new("dev.dryRunStarted", "Dry run started for {0} at {1}. It stops at the lottery confirmation and closes it.");
        public static readonly LocString RealRunStarted = new("dev.realRunStarted", "REAL ENTRY run started for {0} at {1}. Watch it; /ahl stop stops it before Confirm.");
        public static readonly LocString RunRefused = new("dev.runRefused", "The run did not start: {0}");
        public static readonly LocString GrantWarning = new("dev.grantWarning", "REAL ENTRY ALLOWED for this session only: {0} may enter {1} once, for at most {2}. A /ahl dev run on that plot with the word real deposits real gil and cannot be undone. /ahl dev revoke-real-entry withdraws it.");
        public static readonly LocString GrantRefused = new("dev.grantRefused", "Not allowed: name the logged-in character, a district, a ward, a plot on its home world and a gil ceiling above zero.");
        public static readonly LocString Revoked = new("dev.revoked", "Real entry permission withdrawn. Every run is a dry run again.");
        public static readonly LocString SettleStarted = new("dev.settleStarted", "{0} started for {1}. It is a dry run: it reads the result and closes it.");
        public static readonly LocString SettleRealStarted = new("dev.settleRealStarted", "{0} started for {1}, accepting the result where a real permission covers it. Watch it; a press already made is verified, never undone.");
        public static readonly LocString SettleRefused = new("dev.settleRefused", "The visit did not start: {0}");
        public static readonly LocString SettleNotFound = new("dev.settleNotFound", "No unsettled entry of the logged-in character matches that receipt or address.");
        public static readonly LocString RefundGrantWarning = new("dev.refundGrantWarning", "REAL REFUND ALLOWED for this session only: {0} may accept the refund at {1} once. A /ahl dev settle on that plot with the word real, or an automatic refund, presses Yes. /ahl dev revoke-real-settlement withdraws it.");
        public static readonly LocString ClaimGrantWarning = new("dev.claimGrantWarning", "REAL CLAIM ALLOWED for this session only: {0} may claim {1} once. A /ahl dev settle on that plot with the words claim and real presses Yes and cannot be undone. /ahl dev revoke-real-settlement withdraws it.");
        public static readonly LocString SettlementGrantRefused = new("dev.settlementGrantRefused", "Not allowed: name the logged-in character and the district, ward and plot of one of its unsettled entries on its home world.");
        public static readonly LocString SettlementRevoked = new("dev.settlementRevoked", "Real refund and claim permissions withdrawn. Every settlement is a dry run again.");
        public static readonly LocString SwitchingWarning = new("dev.switchingWarning", "CHARACTER SWITCHING ALLOWED for the next batch only: it may log out and log in that batch's characters through Lifestream. It ends when that batch finishes, is discarded or fails a switch, or after two hours. Every batch entry stays a dry run until batches are validated in game. /ahl dev revoke-switching withdraws it.");
        public static readonly LocString SwitchingRevoked = new("dev.switchingRevoked", "Character switching permissions withdrawn, for batches and for the refund queue, and any switch they started is stopped. Each character is processed at its own login again.");
        public static readonly LocString RefundSwitchingWarning = new("dev.refundSwitchingWarning", "REFUND QUEUE SWITCHING ALLOWED for two hours: with Switch characters to collect refunds on, the refund queue may log out and log in the characters ticked on the Characters list through Lifestream to check their results. /ahl dev revoke-switching withdraws it.");
    }
}
