using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Persistence.Model;

// Plot is null only for an entry imported from the Timers text, which names the plot in words the plugin does not
// parse; AddressText keeps those words as the game showed them.
internal sealed record EntryReceipt
{
    public required Guid ReceiptId { get; init; }

    public required ulong CharacterContentId { get; init; }

    public required uint HomeWorldId { get; init; }

    public required CycleKey Cycle { get; init; }

    public required PlotKey? Plot { get; init; }

    public required BuyerType Buyer { get; init; }

    public required ReceiptEvidence Evidence { get; init; }

    public Guid? AttemptId { get; init; }

    public int? EntryNumber { get; init; }

    public long? Deposit { get; init; }

    public DateTime? EnteredUtc { get; init; }

    public string? AddressText { get; init; }

    public bool Imported => Evidence == ReceiptEvidence.ImportedFromGame;

    public bool Matches(ActionAttempt attempt)
    {
        return CharacterContentId == attempt.CharacterContentId
            && HomeWorldId == attempt.HomeWorldId
            && Cycle == attempt.Cycle
            && Plot == attempt.Plot
            && Buyer == attempt.Buyer;
    }

    public bool HoldsSlotOf(ulong characterContentId, uint homeWorldId, CycleKey cycle)
        => CharacterContentId == characterContentId && HomeWorldId == homeWorldId && Cycle == cycle;
}
