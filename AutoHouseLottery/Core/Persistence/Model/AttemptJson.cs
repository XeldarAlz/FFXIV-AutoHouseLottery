using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Persistence.Model;

internal readonly struct AttemptJson
{
    public Guid? Id { get; init; }

    public ActionKind? Kind { get; init; }

    public Guid? Run { get; init; }

    public ulong? Character { get; init; }

    public uint? HomeWorld { get; init; }

    public CycleKeyJson? Cycle { get; init; }

    public PlotKeyJson? Plot { get; init; }

    public BuyerType? Buyer { get; init; }

    public long? Amount { get; init; }

    public DateTime? PreparedUtc { get; init; }

    public AttemptState? State { get; init; }

    public DateTime? UpdatedUtc { get; init; }

    public Guid? Receipt { get; init; }

    public long? GilBefore { get; init; }
}
