using AutoHouseLottery.Core.External;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Core.Tasks.Model;

namespace AutoHouseLottery.Core.Tasks;

// The plain words for the run's stages, refusals and failures, so every window and command says them the same way.
internal static class RunText
{
    public static string Stage(RunStage stage) => Loc.T(stage switch
    {
        RunStage.Travelling => L.Run.StageTravelling,
        RunStage.Approaching => L.Run.StageApproaching,
        RunStage.Checking => L.Run.StageChecking,
        RunStage.Entering => L.Run.StageEntering,
        RunStage.Verifying => L.Run.StageVerifying,
        _ => L.Run.StageGettingReady,
    });

    // "Step 3 of 5: Checking eligibility"; the getting-ready stage has no number.
    public static string StepLine(EntryStep step)
    {
        var stage = EntrySteps.StageOf(step);
        return stage == RunStage.GettingReady ? Stage(stage) : Loc.T(L.Run.StepOf, (int)stage, EntrySteps.StageCount, Stage(stage));
    }

    public static string Refusal(StartRefusal refusal) => refusal == StartRefusal.AutoAnswerPlugin ? AutoAnswer() : Loc.T(refusal switch
    {
        StartRefusal.AlreadyRunning => L.RunStart.AlreadyRunning,
        StartRefusal.Busy => L.RunStart.Busy,
        StartRefusal.NotLoggedIn => L.RunStart.NotLoggedIn,
        StartRefusal.InContent => L.RunStart.InContent,
        StartRefusal.WrongMode => L.RunStart.WrongMode,
        StartRefusal.InvalidPolicy => L.RunStart.InvalidPolicy,
        StartRefusal.CharacterNotInPolicy => L.RunStart.CharacterNotInPolicy,
        StartRefusal.JournalUnavailable => L.RunStart.JournalUnavailable,
        StartRefusal.ReconciliationPending => L.RunStart.ReconciliationPending,
        StartRefusal.AlreadyEntered => L.RunStart.AlreadyEntered,
        StartRefusal.EntryPeriodClosed => L.RunStart.EntryPeriodClosed,
        StartRefusal.MultipleWinsNeedConfirmation => L.RunStart.MultipleWins,
        _ => L.RunStart.NoAssignment,
    });

    public static string Failure(EntryFailure failure, HousingText? rejection) => failure == EntryFailure.Rejected && rejection is { } refused
        ? Loc.T(Rejection(refused))
        : failure == EntryFailure.AutoAnswerPlugin ? AutoAnswer() : Loc.T(failure switch
        {
            EntryFailure.Sold => L.RunFailure.Sold,
            EntryFailure.WrongPhase => L.RunFailure.WrongPhase,
            EntryFailure.NotLottery => L.RunFailure.NotLottery,
            EntryFailure.ChangedPrice => L.RunFailure.ChangedPrice,
            EntryFailure.OverBudget => L.RunFailure.OverBudget,
            EntryFailure.Ineligible => L.RunFailure.Ineligible,
            EntryFailure.TravelFailed => L.RunFailure.TravelFailed,
            EntryFailure.TravelUnavailable => L.RunFailure.TravelUnavailable,
            EntryFailure.DependencyBusy => L.RunFailure.DependencyBusy,
            EntryFailure.WrongDestination => L.RunFailure.WrongDestination,
            EntryFailure.PlacardNotFound => L.RunFailure.PlacardNotFound,
            EntryFailure.PlacardNotOpened => L.RunFailure.PlacardNotOpened,
            EntryFailure.WrongPlacard => L.RunFailure.WrongPlacard,
            EntryFailure.IdentityUnverified => L.RunFailure.IdentityUnverified,
            EntryFailure.SaleUnreadable => L.RunFailure.SaleUnreadable,
            EntryFailure.EntryButtonUnavailable => L.RunFailure.EntryButtonUnavailable,
            EntryFailure.ConfirmationNotShown => L.RunFailure.ConfirmationNotShown,
            EntryFailure.ConfirmationNotReady => L.RunFailure.ConfirmationNotReady,
            EntryFailure.DepositUnreadable => L.RunFailure.DepositUnreadable,
            EntryFailure.GilUnknown => L.RunFailure.GilUnknown,
            EntryFailure.CheckboxNotTicked => L.RunFailure.CheckboxNotTicked,
            EntryFailure.ConfirmRefused => L.RunFailure.ConfirmRefused,
            EntryFailure.RealEntryNotCovered => L.RunFailure.RealEntryNotCovered,
            EntryFailure.CharacterIneligible => L.RunFailure.CharacterIneligible,
            EntryFailure.AlreadyEntered => L.RunFailure.AlreadyEntered,
            EntryFailure.CollectRefundFirst => L.RunFailure.CollectRefundFirst,
            EntryFailure.RefundDryRun => L.RunFailure.RefundDryRun,
            EntryFailure.UnclaimedWin => L.RunFailure.UnclaimedWin,
            EntryFailure.CycleChanged => L.RunFailure.CycleChanged,
            EntryFailure.JournalUnavailable => L.RunFailure.JournalUnavailable,
            EntryFailure.CharacterChanged => L.RunFailure.CharacterChanged,
            EntryFailure.InspectionsExhausted => L.RunFailure.InspectionsExhausted,
            EntryFailure.SubmissionUncertain => L.RunFailure.SubmissionUncertain,
            _ => L.RunFailure.Rejected,
        });

    // The reason anything that opens a lottery dialog is held while a plugin that answers dialogs is loaded.
    public static string AutoAnswer() => Loc.T(L.RunStart.AutoAnswer, AutoAnswerGuard.LoadedName() ?? AutoAnswerPlugins.InternalNames[0]);

    public static string Reconcile(in ReconcileReport report) => report.Outcome switch
    {
        ReconcileOutcome.NotHomeWorld => Loc.T(L.Reconcile.NotHomeWorld),
        ReconcileOutcome.NotRead or ReconcileOutcome.NoCharacter => Loc.T(L.Reconcile.NotRead),
        ReconcileOutcome.Settled => Loc.T(L.Reconcile.Settled, report.Confirmed, report.Rejected),
        _ => Loc.T(L.Reconcile.StillUncertain),
    };

    private static LocString Rejection(HousingText refused) => refused switch
    {
        HousingText.OneEntryPerPeriod => L.RunFailure.RejectedOneEntry,
        HousingText.OutstandingPropertyOrRefund => L.RunFailure.RejectedOutstanding,
        HousingText.LandOwnedOnAccount => L.RunFailure.RejectedLandOwned,
        HousingText.CaretakerItemsUnclaimed => L.RunFailure.RejectedCaretaker,
        HousingText.PurchaseBlockedAfterDemolition => L.RunFailure.RejectedDemolition,
        HousingText.FreeCompanyMembershipTooRecent => L.RunFailure.RejectedMembership,
        HousingText.HousingActionFailed => L.RunFailure.RejectedActionFailed,
        HousingText.FreeCompanyLeft => L.RunFailure.RejectedLeftCompany,
        _ => L.RunFailure.Rejected,
    };
}
