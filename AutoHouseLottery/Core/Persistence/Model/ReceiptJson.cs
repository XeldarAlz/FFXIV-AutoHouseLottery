using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Persistence.Model;

internal readonly struct ReceiptJson
{
    public Guid? Id { get; init; }

    public ulong? Character { get; init; }

    public uint? HomeWorld { get; init; }

    public CycleKeyJson? Cycle { get; init; }

    public PlotKeyJson? Plot { get; init; }

    public BuyerType? Buyer { get; init; }

    public ReceiptEvidence? Evidence { get; init; }

    public Guid? Attempt { get; init; }

    public int? EntryNumber { get; init; }

    public long? Deposit { get; init; }

    public DateTime? EnteredUtc { get; init; }
}
