using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Core.Settlement.Model;

namespace AutoHouseLottery.Core.Settlement;

// The plain words for settlement visits, their steps, outcomes and refusals and the scheduler's waits, so every window
// and command says them the same way.
internal static class SettlementText
{
    public static string Title(SettlementMode mode) => Loc.T(mode switch
    {
        SettlementMode.Refund => L.Settlement.TitleRefund,
        SettlementMode.Claim => L.Settlement.TitleClaim,
        _ => L.Settlement.TitleInspect,
    });

    public static string Step(SettlementStep step) => Loc.T(step switch
    {
        SettlementStep.Travel or SettlementStep.VerifyArrival => L.Run.StageTravelling,
        SettlementStep.OpenPlacard => L.Run.StageApproaching,
        SettlementStep.ViewResult or SettlementStep.Classify => L.Settlement.StepReading,
        SettlementStep.PersistIntent or SettlementStep.Press => L.Settlement.StepPressing,
        SettlementStep.AwaitSettlement => L.Run.FinishingVerification,
        _ => L.Run.StageGettingReady,
    });

    public static string Outcome(SettlementStatus status, string address) => status.Outcome switch
    {
        InspectionOutcome.WonUnclaimed => Loc.T(L.Settlement.OutcomeWon, address),
        InspectionOutcome.LostRefundDue => Loc.T(L.Settlement.OutcomeLost, address, Loc.Gil(status.OfferedRefund ?? 0)),
        InspectionOutcome.ForfeitRefundDue => Loc.T(L.Settlement.OutcomeForfeit, address, Loc.Gil(status.OfferedRefund ?? 0)),
        InspectionOutcome.Expired => Loc.T(L.Settlement.OutcomeExpired, address),
        InspectionOutcome.Claimed => Loc.T(L.Settlement.OutcomeClaimed, address),
        InspectionOutcome.Refunded => Loc.T(L.Settlement.OutcomeRefunded, address),
        _ => Loc.T(L.Settlement.OutcomeNotEntered, address),
    };

    public static string Failure(SettlementFailure failure) => failure == SettlementFailure.AutoAnswerPlugin ? Tasks.RunText.AutoAnswer() : Loc.T(failure switch
    {
        SettlementFailure.CharacterChanged => L.Settlement.FailureCharacter,
        SettlementFailure.JournalUnavailable => L.RunFailure.JournalUnavailable,
        SettlementFailure.NothingToSettle => L.Settlement.FailureNothing,
        SettlementFailure.ResultsNotOut => L.Settlement.FailureResultsNotOut,
        SettlementFailure.AttemptInQuestion => L.Settlement.FailureInQuestion,
        SettlementFailure.PlotUnknown => L.Settlement.FailurePlotUnknown,
        SettlementFailure.GilUnknown => L.RunFailure.GilUnknown,
        SettlementFailure.BelowReserve => L.Settlement.FailureReserve,
        SettlementFailure.TravelBudgetSpent => L.Settlement.FailureTravelBudget,
        SettlementFailure.TravelFailed => L.RunFailure.TravelFailed,
        SettlementFailure.TravelUnavailable => L.RunFailure.TravelUnavailable,
        SettlementFailure.DependencyBusy => L.RunFailure.DependencyBusy,
        SettlementFailure.WrongDestination => L.RunFailure.WrongDestination,
        SettlementFailure.PlacardNotFound => L.RunFailure.PlacardNotFound,
        SettlementFailure.PlacardNotOpened => L.RunFailure.PlacardNotOpened,
        SettlementFailure.WrongPlacard => L.RunFailure.WrongPlacard,
        SettlementFailure.PlacardUnverified => L.RunFailure.IdentityUnverified,
        SettlementFailure.NoResultOffered => L.Settlement.FailureNoResult,
        SettlementFailure.ResultNotShown => L.Settlement.FailureResultNotShown,
        SettlementFailure.UnrecognizedDialog => L.Settlement.FailureUnknownDialog,
        SettlementFailure.AmountUnreadable => L.Settlement.FailureAmount,
        SettlementFailure.ContradictsRecord => L.Settlement.FailureContradicts,
        SettlementFailure.RefundDiffersFromDeposit => L.Settlement.FailureRefundDiffers,
        SettlementFailure.PressRefused => L.Settlement.FailurePressRefused,
        SettlementFailure.NotTheEntrant => L.Settlement.FailureNotEntrant,
        SettlementFailure.BuyerUnknown => L.Settlement.FailureBuyerUnknown,
        SettlementFailure.ClaimEffectUnknown => L.Settlement.FailureEffectUnknown,
        SettlementFailure.GroupClaimOpen => L.Settlement.FailureGroupClaim,
        SettlementFailure.AlreadyOwned => L.Settlement.FailureAlreadyOwned,
        SettlementFailure.DepositUnknown => L.Settlement.FailureDepositUnknown,
        SettlementFailure.PausedForInput => L.Settlement.FailurePausedForInput,
        SettlementFailure.StoppedByPlayer => L.Settlement.FailureStopped,
        _ => L.Settlement.FailureInterrupted,
    });

    public static string Refusal(SettlementRefusal refusal) => refusal == SettlementRefusal.AutoAnswerPlugin ? Tasks.RunText.AutoAnswer() : Loc.T(refusal switch
    {
        SettlementRefusal.NotLoggedIn => L.RunStart.NotLoggedIn,
        SettlementRefusal.UnsupportedRegion => L.RunStart.UnsupportedRegion,
        SettlementRefusal.InContent => L.RunStart.InContent,
        SettlementRefusal.JournalUnavailable => L.RunStart.JournalUnavailable,
        _ => L.RunStart.Busy,
    });

    public static string Block(SchedulerBlock block) => block == SchedulerBlock.AutoAnswerPlugin ? Tasks.RunText.AutoAnswer() : Loc.T(block switch
    {
        SchedulerBlock.RefundsOff => L.Settlement.BlockOff,
        SchedulerBlock.NothingDue => L.Settlement.BlockNothingDue,
        SchedulerBlock.JournalUnavailable => L.RunStart.JournalUnavailable,
        SchedulerBlock.InDuty => L.Settlement.BlockDuty,
        SchedulerBlock.InCombat => L.Settlement.BlockCombat,
        SchedulerBlock.InCutscene => L.Settlement.BlockCutscene,
        SchedulerBlock.Busy => L.Settlement.BlockBusy,
        SchedulerBlock.Occupied => L.Settlement.BlockOccupied,
        SchedulerBlock.Typing => L.Settlement.BlockTyping,
        SchedulerBlock.NotIdle => L.Settlement.BlockNotIdle,
        SchedulerBlock.BelowReserve => L.Settlement.FailureReserve,
        SchedulerBlock.TravelBudgetSpent => L.Settlement.FailureTravelBudget,
        SchedulerBlock.None => L.Settlement.BlockStarting,
        _ => L.RunStart.NotLoggedIn,
    });
}
