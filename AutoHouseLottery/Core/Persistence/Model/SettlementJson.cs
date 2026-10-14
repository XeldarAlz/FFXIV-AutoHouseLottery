namespace AutoHouseLottery.Core.Persistence.Model;

internal readonly struct SettlementJson
{
    public Guid? Receipt { get; init; }

    public SettlementState? State { get; init; }

    public RefundKind? Refund { get; init; }

    public long? ExpectedRefund { get; init; }

    public long? ReceivedRefund { get; init; }

    public DateTime? UpdatedUtc { get; init; }

    public long? TravelSpent { get; init; }

    public DateTime? RefundDeadlineUtc { get; init; }

    public bool? SettledByAbsence { get; init; }
}
