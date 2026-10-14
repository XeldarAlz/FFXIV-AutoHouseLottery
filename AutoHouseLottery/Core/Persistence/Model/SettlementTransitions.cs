namespace AutoHouseLottery.Core.Persistence.Model;

internal static class SettlementTransitions
{
    public static bool IsLegal(SettlementState from, SettlementState to) => from switch
    {
        SettlementState.Entered => to is SettlementState.ResultsPending,
        SettlementState.ResultsPending => to is SettlementState.InspectingResult,
        SettlementState.InspectingResult => to is SettlementState.WonUnclaimed
            or SettlementState.LostRefundDue
            or SettlementState.ForfeitRefundDue
            or SettlementState.OutcomeUnknown
            or SettlementState.Claimed
            or SettlementState.Refunded
            or SettlementState.Expired,
        SettlementState.OutcomeUnknown => to is SettlementState.InspectingResult,
        SettlementState.WonUnclaimed => to is SettlementState.ClaimPending or SettlementState.ForfeitRefundDue or SettlementState.Claimed,
        SettlementState.ClaimPending => to is SettlementState.Claimed or SettlementState.WonUnclaimed or SettlementState.NeedsReconciliation,
        SettlementState.LostRefundDue => to is SettlementState.RefundPending or SettlementState.Refunded or SettlementState.Expired,
        SettlementState.ForfeitRefundDue => to is SettlementState.RefundPending or SettlementState.Refunded or SettlementState.Expired,
        SettlementState.RefundPending => to is SettlementState.Refunded
            or SettlementState.LostRefundDue
            or SettlementState.ForfeitRefundDue
            or SettlementState.NeedsReconciliation,
        SettlementState.NeedsReconciliation => to is SettlementState.InspectingResult or SettlementState.Claimed or SettlementState.Refunded,
        _ => false,
    };

    public static bool IsSettled(SettlementState state) => state is SettlementState.Claimed or SettlementState.Refunded or SettlementState.Expired;
}
