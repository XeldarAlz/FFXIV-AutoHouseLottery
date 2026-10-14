using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Settlement.Model;

// One visit to an entered plot, from the character check to the settled outcome.
internal enum SettlementStep : byte
{
    Ready,
    Preflight,
    ResolvePlot,
    Travel,
    VerifyArrival,
    OpenPlacard,
    ViewResult,
    Classify,
    PersistIntent,
    Press,
    AwaitSettlement,
    Finished,
}

// Classified: the result was read and recorded, and nothing was pressed. DryRun: a refund or claim was due, and the
// dialog was closed instead because nothing permits the press. Refunded, RefundedAmountDiffers and Claimed: the press
// went through and was confirmed. NeedsReconciliation: a press may have reached the game and its outcome is not known.
// NeedsPlayer: the visit stopped short and says why. Stopped: the visit was stopped before it pressed anything.
internal enum SettlementResult : byte
{
    None,
    Classified,
    DryRun,
    Refunded,
    RefundedAmountDiffers,
    Claimed,
    NeedsReconciliation,
    NeedsPlayer,
    Stopped,
}

internal enum SettlementFailure : byte
{
    None,
    CharacterChanged,
    JournalUnavailable,
    NothingToSettle,
    AttemptInQuestion,
    PlotUnknown,
    GilUnknown,
    BelowReserve,
    TravelBudgetSpent,
    TravelFailed,
    TravelUnavailable,
    DependencyBusy,
    WrongDestination,
    PlacardNotFound,
    PlacardNotOpened,
    WrongPlacard,
    PlacardUnverified,
    NoResultOffered,
    ResultNotShown,
    UnrecognizedDialog,
    AmountUnreadable,
    ContradictsRecord,
    RefundDiffersFromDeposit,
    PressRefused,
    NotTheEntrant,
    BuyerUnknown,
    ClaimEffectUnknown,
    GroupClaimOpen,
    AlreadyOwned,
    PausedForInput,
    StoppedByPlayer,
    Interrupted,
}

internal static class SettlementSteps
{
    // From the written intent to the settled outcome a press may have reached the game, so neither a pause nor a stop may
    // cut the visit short there; it finishes verifying first.
    public static bool InPressWindow(SettlementStep step) => step is SettlementStep.PersistIntent or SettlementStep.Press or SettlementStep.AwaitSettlement;

    public static bool MayInterrupt(SettlementStep step) => !InPressWindow(step);

    public static SettlementFailure FromInspection(InspectionReason reason) => reason switch
    {
        InspectionReason.WrongPlacard => SettlementFailure.WrongPlacard,
        InspectionReason.PlacardUnverified => SettlementFailure.PlacardUnverified,
        InspectionReason.NoResultOffered => SettlementFailure.NoResultOffered,
        InspectionReason.ResultNotShown => SettlementFailure.ResultNotShown,
        InspectionReason.UnrecognizedDialog => SettlementFailure.UnrecognizedDialog,
        InspectionReason.AmountUnreadable => SettlementFailure.AmountUnreadable,
        InspectionReason.ContradictsRecord => SettlementFailure.ContradictsRecord,
        _ => SettlementFailure.None,
    };

    public static SettlementFailure FromClaim(ClaimRefusal refusal) => refusal switch
    {
        ClaimRefusal.NotTheEntrant => SettlementFailure.NotTheEntrant,
        ClaimRefusal.PlotUnknown => SettlementFailure.PlotUnknown,
        ClaimRefusal.NotWon => SettlementFailure.NothingToSettle,
        ClaimRefusal.BuyerUnknown => SettlementFailure.BuyerUnknown,
        ClaimRefusal.EffectUnknown => SettlementFailure.ClaimEffectUnknown,
        ClaimRefusal.GroupClaimOpen => SettlementFailure.GroupClaimOpen,
        ClaimRefusal.AlreadyOwned => SettlementFailure.AlreadyOwned,
        _ => SettlementFailure.None,
    };
}

// The published state of one settlement visit, rebuilt on every change and read by the windows. Real: the press this
// visit is for would be allowed when it comes to it. OfferedRefund: what the result dialog offered; Received: what the
// gil showed coming back.
internal sealed record SettlementStatus
{
    public static readonly SettlementStatus Idle = new();

    public bool Active { get; init; }

    public Guid VisitId { get; init; }

    public SettlementMode Mode { get; init; }

    public SettlementOrigin Origin { get; init; }

    public SettlementStep Step { get; init; }

    public Guid ReceiptId { get; init; }

    public ulong ContentId { get; init; }

    public string CharacterName { get; init; } = string.Empty;

    public PlotKey? Plot { get; init; }

    public string? AddressText { get; init; }

    public bool Real { get; init; }

    public SettlementResult Result { get; init; }

    public SettlementFailure Failure { get; init; }

    public InspectionOutcome? Outcome { get; init; }

    public long? OfferedRefund { get; init; }

    public long? Received { get; init; }

    public ClaimEffect Effect { get; init; }

    public long TravelSpent { get; init; }

    public bool StopRequested { get; init; }

    public DateTime StartedUtc { get; init; }

    public bool FinishingVerification => Active && SettlementSteps.InPressWindow(Step);
}
