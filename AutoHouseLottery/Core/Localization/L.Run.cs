namespace AutoHouseLottery.Core.Localization;

internal static partial class L
{
    internal static class Run
    {
        public static readonly LocString Address = new("run.address", "{0} · Ward {1} · Plot {2}");
        public static readonly LocString Title = new("run.title", "Entering lottery · Character {0} of {1}");
        public static readonly LocString StepOf = new("run.stepOf", "Step {0} of {1}: {2}");
        public static readonly LocString StageGettingReady = new("run.stage.gettingReady", "Getting ready");
        public static readonly LocString StageTravelling = new("run.stage.travelling", "Travelling");
        public static readonly LocString StageApproaching = new("run.stage.approaching", "Approaching plot");
        public static readonly LocString StageChecking = new("run.stage.checking", "Checking eligibility");
        public static readonly LocString StageEntering = new("run.stage.entering", "Entering lottery");
        public static readonly LocString StageVerifying = new("run.stage.verifying", "Verifying entry");
        public static readonly LocPlural ConfirmedEntries = new("run.confirmedEntries", "{0} confirmed entry", "{0} confirmed entries");
        public static readonly LocString Deposited = new("run.deposited", "{0} deposited");
        public static readonly LocString Remaining = new("run.remaining", "up to {0} remaining");
        public static readonly LocString Inspection = new("run.inspection", "Plot {0} of {1} on the shortlist");
        public static readonly LocString FinishingVerification = new("run.finishingVerification", "Finishing verification");
        public static readonly LocString PausedLabel = new("run.paused", "Paused before the next step");
        public static readonly LocString DryRunBadge = new("run.dryRunBadge", "DRY RUN");
        public static readonly LocString RealEntryBadge = new("run.realEntryBadge", "REAL ENTRY");
        public static readonly LocString DryRunHint = new("run.dryRunHint", "Dry run: it goes as far as the lottery confirmation and closes it. Nothing is paid.");
        public static readonly LocString RealEntryHint = new("run.realEntryHint", "Enters for real: the deposit is paid at the confirmation and cannot be taken back.");
        public static readonly LocString ShowDetails = new("run.showDetails", "Show details");
        public static readonly LocString HideDetails = new("run.hideDetails", "Hide details");
        public static readonly LocString ModeExact = new("run.mode.exact", "Exact plot");
        public static readonly LocString ModeBestMatch = new("run.mode.bestMatch", "Best match");
        public static readonly LocString Elapsed = new("run.elapsed", "Running for {0}");
        public static readonly LocString LiveDeposit = new("run.liveDeposit", "Deposit on the confirmation: {0}");
        public static readonly LocString EnteredTitle = new("run.enteredTitle", "Entered the lottery");
        public static readonly LocString EnteredBody = new("run.enteredBody", "{0} is entered for {1}. My lottery keeps the receipt.");
        public static readonly LocString DryRunTitle = new("run.dryRunTitle", "Dry run finished");
        public static readonly LocString DryRunBody = new("run.dryRunBody", "At the confirmation for {0}, the run would have entered for {1}. It closed the confirmation instead and nothing was paid.");
        public static readonly LocString DryRunGilMoved = new("run.dryRunGilMoved", "Your gil changed while the confirmation was open. Check the Console before trusting this dry run.");
        public static readonly LocString NoEntryTitle = new("run.noEntryTitle", "No entry this time");
        public static readonly LocString StoppedTitle = new("run.stoppedTitle", "Run stopped");
        public static readonly LocString NeedsPlayerTitle = new("run.needsPlayerTitle", "Waiting for you");
        public static readonly LocString UncertainTitle = new("run.uncertainTitle", "Checking whether your entry completed");
        public static readonly LocString UncertainBody = new("run.uncertainBody", "The game may have accepted the entry, but the plugin could not confirm it. Retry entry stays unavailable until your lottery entry has been read back.");
        public static readonly LocString CheckAgain = new("run.checkAgain", "Check again");
        public static readonly LocString VisitManually = new("run.visitManually", "Visit manually");
        public static readonly LocString ReviewChanges = new("run.reviewChanges", "Review changes");
        public static readonly LocString RetryTravel = new("run.retryTravel", "Retry travel");
        public static readonly LocString Dismiss = new("run.dismiss", "Dismiss");
        public static readonly LocString OpenMyLottery = new("run.openMyLottery", "Open My lottery");
        public static readonly LocString Checking = new("run.checking", "Checking your earlier entry");
    }

    internal static class RunFailure
    {
        public static readonly LocString Sold = new("runFailure.sold", "The plot is no longer for sale.");
        public static readonly LocString WrongPhase = new("runFailure.wrongPhase", "The plot is not taking entries right now.");
        public static readonly LocString NotLottery = new("runFailure.notLottery", "The plot is sold first come, first served, not by lottery.");
        public static readonly LocString ChangedPrice = new("runFailure.changedPrice", "The plot's deposit changed since you chose it.");
        public static readonly LocString OverBudget = new("runFailure.overBudget", "The deposit is more than this character's limit or gil allows.");
        public static readonly LocString Ineligible = new("runFailure.ineligible", "This plot is for a different buyer type.");
        public static readonly LocString TravelFailed = new("runFailure.travelFailed", "Travel did not reach the plot.");
        public static readonly LocString TravelUnavailable = new("runFailure.travelUnavailable", "Travel needs Lifestream. Install it, or walk to the plot yourself.");
        public static readonly LocString DependencyBusy = new("runFailure.dependencyBusy", "Lifestream is busy with something else.");
        public static readonly LocString WrongDestination = new("runFailure.wrongDestination", "The character is not in the plot's ward.");
        public static readonly LocString PlacardNotFound = new("runFailure.placardNotFound", "The plot's placard could not be found nearby.");
        public static readonly LocString PlacardNotOpened = new("runFailure.placardNotOpened", "The plot's placard did not open.");
        public static readonly LocString WrongPlacard = new("runFailure.wrongPlacard", "The open placard belongs to another plot.");
        public static readonly LocString IdentityUnverified = new("runFailure.identityUnverified", "The placard's address could not be confirmed.");
        public static readonly LocString SaleUnreadable = new("runFailure.saleUnreadable", "The placard's sale details could not be read.");
        public static readonly LocString EntryButtonUnavailable = new("runFailure.entryButtonUnavailable", "The placard offers no lottery entry to this character.");
        public static readonly LocString ConfirmationNotShown = new("runFailure.confirmationNotShown", "The lottery confirmation did not appear.");
        public static readonly LocString ConfirmationNotReady = new("runFailure.confirmationNotReady", "The lottery confirmation changed before Confirm could be pressed. Nothing was submitted.");
        public static readonly LocString DepositUnreadable = new("runFailure.depositUnreadable", "The deposit on the lottery confirmation could not be read.");
        public static readonly LocString GilUnknown = new("runFailure.gilUnknown", "This character's gil could not be read.");
        public static readonly LocString CheckboxNotTicked = new("runFailure.checkboxNotTicked", "The confirmation's checkbox could not be ticked. Nothing was submitted.");
        public static readonly LocString ConfirmRefused = new("runFailure.confirmRefused", "Confirm could not be pressed. Nothing was submitted.");
        public static readonly LocString RealEntryNotCovered = new("runFailure.realEntryNotCovered", "This entry is not covered by the real-entry permission. Nothing was submitted.");
        public static readonly LocString Rejected = new("runFailure.rejected", "The game refused the entry.");
        public static readonly LocString CharacterIneligible = new("runFailure.characterIneligible", "This character cannot enter this lottery.");
        public static readonly LocString AlreadyEntered = new("runFailure.alreadyEntered", "This character already has an entry this lottery period.");
        public static readonly LocString CollectRefundFirst = new("runFailure.collectRefundFirst", "Collect refund first. The game refuses a new entry until an earlier refund is collected.");
        public static readonly LocString RefundDryRun = new("runFailure.refundDryRun", "A dry run cannot collect the earlier refund, so the entry stops here. The refund is still due.");
        public static readonly LocString CheckEarlierResult = new("runFailure.checkEarlierResult", "Check your earlier entry's result first. Its deposit is still held.");
        public static readonly LocString UnclaimedWin = new("runFailure.unclaimedWin", "You won an earlier lottery. Claim that plot or give it up before entering again.");
        public static readonly LocString CycleChanged = new("runFailure.cycleChanged", "A new lottery period began during the run.");
        public static readonly LocString JournalUnavailable = new("runFailure.journalUnavailable", "Entries cannot be recorded right now, so none is made.");
        public static readonly LocString CharacterChanged = new("runFailure.characterChanged", "A different character is logged in.");
        public static readonly LocString InspectionsExhausted = new("runFailure.inspectionsExhausted", "No plot on the shortlist passed its live checks.");
        public static readonly LocString SubmissionUncertain = new("runFailure.submissionUncertain", "The entry's outcome is not known yet.");
        public static readonly LocString RejectedOneEntry = new("runFailure.rejected.oneEntry", "The game says this character already has an entry this lottery period.");
        public static readonly LocString RejectedOutstanding = new("runFailure.rejected.outstanding", "The game says an earlier refund or plot must be collected first.");
        public static readonly LocString RejectedLandOwned = new("runFailure.rejected.landOwned", "The game says this account already owns land on this world.");
        public static readonly LocString RejectedCaretaker = new("runFailure.rejected.caretaker", "The game says the caretaker still holds items for you.");
        public static readonly LocString RejectedDemolition = new("runFailure.rejected.demolition", "The game says land cannot be bought right after an automatic demolition.");
        public static readonly LocString RejectedMembership = new("runFailure.rejected.membership", "The game says a free company plot needs 30 days of membership.");
        public static readonly LocString RejectedActionFailed = new("runFailure.rejected.actionFailed", "The game could not complete the housing action. Try again later.");
        public static readonly LocString RejectedLeftCompany = new("runFailure.rejected.leftCompany", "The game says this character no longer belongs to that free company.");
    }

    internal static class RunStart
    {
        public static readonly LocString AlreadyRunning = new("runStart.alreadyRunning", "A run is already in progress.");
        public static readonly LocString Busy = new("runStart.busy", "Another task is using the game.");
        public static readonly LocString NotLoggedIn = new("runStart.notLoggedIn", "Log in to a character first.");
        public static readonly LocString UnsupportedRegion = new("runStart.unsupportedRegion", "Housing on Chinese and Korean worlds is not supported.");
        public static readonly LocString InContent = new("runStart.inContent", "Leave the duty first.");
        public static readonly LocString WrongMode = new("runStart.wrongMode", "This plan does not match the button used.");
        public static readonly LocString InvalidPolicy = new("runStart.invalidPolicy", "The plan is incomplete.");
        public static readonly LocString CharacterNotInPolicy = new("runStart.characterNotInPolicy", "The logged-in character is not part of this plan.");
        public static readonly LocString JournalUnavailable = new("runStart.journalUnavailable", "Entries cannot be recorded right now.");
        public static readonly LocString ReconciliationPending = new("runStart.reconciliationPending", "An earlier entry is still being checked.");
        public static readonly LocString AlreadyEntered = new("runStart.alreadyEntered", "This character already has an entry this lottery period.");
        public static readonly LocString EntryPeriodClosed = new("runStart.entryPeriodClosed", "The lottery is not taking entries right now.");
        public static readonly LocString NoAssignment = new("runStart.noAssignment", "No plot in this plan can be entered by this character.");
        public static readonly LocString MultipleWins = new("runStart.multipleWins", "Confirm first that only one win can be claimed.");
        public static readonly LocString AutoAnswer = new("runStart.autoAnswer", "Turn off {0} first: it could answer the lottery dialogs.");
    }

    internal static class Reconcile
    {
        public static readonly LocString NotHomeWorld = new("reconcile.notHomeWorld", "Return to your home world, then check again.");
        public static readonly LocString NotRead = new("reconcile.notRead", "Your lottery entry could not be read. Try again in a moment.");
        public static readonly LocString StillUncertain = new("reconcile.stillUncertain", "Your lottery entry does not show yet whether it completed.");
        public static readonly LocString Settled = new("reconcile.settled", "Checked: {0} confirmed, {1} not entered.");
    }
}
