namespace AutoHouseLottery.Core.Localization;

internal static partial class L
{
    internal static class Lottery
    {
        public static readonly LocString ActionNeeded = new("lottery.actionNeeded", "Action needed");
        public static readonly LocString Active = new("lottery.active", "Active entries");
        public static readonly LocString History = new("lottery.history", "History ({0})");
        public static readonly LocString UnknownCharacter = new("lottery.unknownCharacter", "Unknown character");
        public static readonly LocString PlotNotNamed = new("lottery.plotNotNamed", "Plot not named by the game");
        public static readonly LocString Number = new("lottery.number", "Lottery number {0}");
        public static readonly LocString NumberUnknown = new("lottery.numberUnknown", "Lottery number unknown");
        public static readonly LocString Deposit = new("lottery.deposit", "Deposit {0}");
        public static readonly LocString DepositUnknown = new("lottery.depositUnknown", "Deposit unknown");
        public static readonly LocString EntriesClose = new("lottery.entriesClose", "Entries close {0}");
        public static readonly LocString ResultsUntil = new("lottery.resultsUntil", "Results until {0}");
        public static readonly LocString DeadlineUnknown = new("lottery.deadlineUnknown", "Deadline unknown");
        public static readonly LocString Imported = new("lottery.imported", "From your Timers");
        public static readonly LocString StateEntered = new("lottery.state.entered", "Entered");
        public static readonly LocString StateResultsAvailable = new("lottery.state.resultsAvailable", "Results available");
        public static readonly LocString StateWon = new("lottery.state.won", "Won");
        public static readonly LocString StateRefundDue = new("lottery.state.refundDue", "Refund due");
        public static readonly LocString StateSettlementPending = new("lottery.state.settlementPending", "Outcome unclear");
        public static readonly LocString StateUncertain = new("lottery.state.uncertain", "Checking whether your entry completed");
        public static readonly LocString StateSettled = new("lottery.state.settled", "Settled");
        public static readonly LocString StateRejected = new("lottery.state.rejected", "Not entered");
        public static readonly LocString ViewPlot = new("lottery.action.viewPlot", "View plot");
        public static readonly LocString CheckResult = new("lottery.action.checkResult", "Check result");
        public static readonly LocString ClaimHouse = new("lottery.action.claimHouse", "Claim house");
        public static readonly LocString CollectRefund = new("lottery.action.collectRefund", "Collect refund");
        public static readonly LocString VerifyOutcome = new("lottery.action.verifyOutcome", "Verify outcome");
        public static readonly LocString ViewReceipt = new("lottery.action.viewReceipt", "View receipt");
        public static readonly LocString ArrivesLater = new("lottery.arrivesLater", "Arrives with the results update");
        public static readonly LocString LogInAs = new("lottery.logInAs", "Log in as {0} to check it.");
        public static readonly LocString EnteredAt = new("lottery.enteredAt", "Entered {0}");
        public static readonly LocString EvidenceConfirmation = new("lottery.evidence.confirmation", "Recorded from the entry confirmation and your lottery entry.");
        public static readonly LocString EvidenceReconciled = new("lottery.evidence.reconciled", "Recorded from a later check of your lottery entry.");
        public static readonly LocString EvidenceImported = new("lottery.evidence.imported", "Found in your Timers; the plugin did not make this entry.");
        public static readonly LocString JournalLocked = new("lottery.journal.locked", "Your entry records are open in another copy of the game. Close it, then reload the plugin. Entries are paused.");
        public static readonly LocString JournalCorrupt = new("lottery.journal.corrupt", "Your entry records could not be read. They are kept as they are; entries are paused.");
        public static readonly LocString JournalRecoveryFailed = new("lottery.journal.recoveryFailed", "Your entry records could not be updated. Entries are paused until the plugin can write them.");
        public static readonly LocString JournalNewerSchema = new("lottery.journal.newerSchema", "Your entry records come from a newer version of the plugin. Update it to see them. Entries are paused.");
        public static readonly LocString JournalMigrationFailed = new("lottery.journal.migrationFailed", "Your entry records could not be upgraded. A backup was kept; entries are paused.");
        public static readonly LocString JournalReadFailed = new("lottery.journal.readFailed", "Your entry records could not be opened. Entries are paused.");
        public static readonly LocString JournalTitle = new("lottery.journal.title", "Entry records unavailable");
    }
}
