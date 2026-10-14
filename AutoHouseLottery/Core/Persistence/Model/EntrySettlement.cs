namespace AutoHouseLottery.Core.Persistence.Model;

internal readonly record struct EntrySettlement(
    Guid ReceiptId,
    SettlementState State,
    RefundKind Refund,
    long? ExpectedRefund,
    long? ReceivedRefund,
    DateTime UpdatedUtc)
{
    // Gil spent travelling to the entered plot to read its result, collect its refund or claim it.
    public long TravelSpent { get; init; }

    // The refund deadline the result dialog itself named, when it could be read.
    public DateTime? RefundDeadlineUtc { get; init; }

    // Settled without a result being read: the placard no longer offered one, or the player said it was settled in game.
    // Such a settlement can be reopened when the game later says a refund or a plot is still waiting (LogMessage 6100).
    public bool SettledByAbsence { get; init; }

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
