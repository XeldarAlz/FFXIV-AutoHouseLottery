using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Persistence.Model;

internal sealed record ActionAttempt
{
    public required Guid AttemptId { get; init; }

    public required ActionKind Kind { get; init; }

    public required Guid RunId { get; init; }

    public required ulong CharacterContentId { get; init; }

    public required uint HomeWorldId { get; init; }

    public required CycleKey Cycle { get; init; }

    public required PlotKey Plot { get; init; }

    public required BuyerType Buyer { get; init; }

    public required long Amount { get; init; }

    public required DateTime PreparedUtc { get; init; }

    public AttemptState State { get; init; } = AttemptState.Prepared;

    public DateTime UpdatedUtc { get; init; }

    public Guid? ReceiptId { get; init; }

    public long? GilBefore { get; init; }

    public OperationKey Key => Kind == ActionKind.Entry
        ? OperationKey.ForEntry(CharacterContentId, HomeWorldId, Cycle)
        : OperationKey.ForSettlement(Kind, ReceiptId ?? Guid.Empty);
}
