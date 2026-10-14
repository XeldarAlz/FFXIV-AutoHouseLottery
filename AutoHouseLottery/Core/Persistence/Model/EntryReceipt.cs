using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Persistence.Model;

internal sealed record EntryReceipt
{
    public required Guid ReceiptId { get; init; }

    public required ulong CharacterContentId { get; init; }

    public required uint HomeWorldId { get; init; }

    public required CycleKey Cycle { get; init; }

    public required PlotKey Plot { get; init; }

    public required BuyerType Buyer { get; init; }

    public required ReceiptEvidence Evidence { get; init; }

    public Guid? AttemptId { get; init; }

    public int? EntryNumber { get; init; }

    public long? Deposit { get; init; }

    public DateTime? EnteredUtc { get; init; }

    public bool Matches(ActionAttempt attempt)
    {
        return CharacterContentId == attempt.CharacterContentId
            && HomeWorldId == attempt.HomeWorldId
            && Cycle == attempt.Cycle
            && Plot == attempt.Plot
            && Buyer == attempt.Buyer;
    }
}
