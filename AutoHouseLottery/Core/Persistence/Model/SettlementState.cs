namespace AutoHouseLottery.Core.Persistence.Model;

internal enum SettlementState : byte
{
    Entered,
    ResultsPending,
    InspectingResult,
    WonUnclaimed,
    LostRefundDue,
    OutcomeUnknown,
    ClaimPending,
    Claimed,
    RefundPending,
    Refunded,
    ForfeitRefundDue,
    Expired,
    NeedsReconciliation,
}
