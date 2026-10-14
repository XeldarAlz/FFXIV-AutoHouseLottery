namespace AutoHouseLottery.Core.Persistence.Model;

internal readonly record struct EntrySettlement(
    Guid ReceiptId,
    SettlementState State,
    RefundKind Refund,
    long? ExpectedRefund,
    long? ReceivedRefund,
    DateTime UpdatedUtc)
{
    public bool IsSettled => SettlementTransitions.IsSettled(State);

    public static EntrySettlement Entered(Guid receiptId, DateTime nowUtc)
    {
        return new EntrySettlement(receiptId, SettlementState.Entered, RefundKind.None, null, null, nowUtc);
    }

    public bool TryAdvance(SettlementState next, long? amount, DateTime nowUtc, out EntrySettlement advanced)
    {
        advanced = this;
        if (!SettlementTransitions.IsLegal(State, next) || amount is <= 0)
        {
            return false;
        }

        var refund = next switch
        {
            SettlementState.LostRefundDue => RefundKind.Loss,
            SettlementState.ForfeitRefundDue => RefundKind.Forfeit,
            _ => Refund,
        };
        if (State == SettlementState.RefundPending && refund != Refund)
        {
            return false;
        }

        advanced = next switch
        {
            SettlementState.LostRefundDue or SettlementState.ForfeitRefundDue => this with { State = next, Refund = refund, ExpectedRefund = amount ?? ExpectedRefund, UpdatedUtc = nowUtc },
            SettlementState.Refunded => this with { State = next, ReceivedRefund = amount, UpdatedUtc = nowUtc },
            _ => this with { State = next, UpdatedUtc = nowUtc },
        };
        return true;
    }
}
