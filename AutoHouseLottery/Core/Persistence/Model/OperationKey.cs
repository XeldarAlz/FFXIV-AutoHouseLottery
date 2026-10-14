using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Persistence.Model;

internal readonly record struct OperationKey
{
    private OperationKey(ActionKind kind, ulong characterContentId, uint homeWorldId, CycleKey cycle, Guid receiptId)
    {
        Kind = kind;
        CharacterContentId = characterContentId;
        HomeWorldId = homeWorldId;
        Cycle = cycle;
        ReceiptId = receiptId;
    }

    public ActionKind Kind { get; }

    public ulong CharacterContentId { get; }

    public uint HomeWorldId { get; }

    public CycleKey Cycle { get; }

    public Guid ReceiptId { get; }

    public static OperationKey ForEntry(ulong characterContentId, uint homeWorldId, CycleKey cycle)
    {
        return new OperationKey(ActionKind.Entry, characterContentId, homeWorldId, cycle, Guid.Empty);
    }

    public static OperationKey ForSettlement(ActionKind kind, Guid receiptId)
    {
        if (kind == ActionKind.Entry)
        {
            throw new ArgumentException("An entry is keyed by character and cycle, not by receipt.", nameof(kind));
        }

        return new OperationKey(kind, 0, 0, default, receiptId);
    }
}
