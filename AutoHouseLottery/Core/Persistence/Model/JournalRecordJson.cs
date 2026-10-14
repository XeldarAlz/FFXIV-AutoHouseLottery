namespace AutoHouseLottery.Core.Persistence.Model;

internal readonly struct JournalRecordJson
{
    public JournalRecordKind? Kind { get; init; }

    public int? Schema { get; init; }

    public AttemptJson? Attempt { get; init; }

    public ReceiptJson? Receipt { get; init; }

    public SettlementJson? Settlement { get; init; }
}
