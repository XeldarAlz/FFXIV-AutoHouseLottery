using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Game.Model;

// Incomplete: the layout fits but some row values are not filled in yet.
internal enum WardReadStatus : byte
{
    Read,
    WindowClosed,
    LayoutMismatch,
    Incomplete,
}

// Unrequested: read as displayed, with no request to tie it to. PacketAndAddon: the ward's packet arrived after the
// request and the window refreshed after the packet.
internal enum WardCorrelation : byte
{
    Unrequested,
    PacketAndAddon,
}

// Status, the tags and UnidentifiedValue are the raw AtkValues; SizeText keeps the game's size glyphs.
internal readonly record struct WardPlotRow(
    int PlotIndex,
    uint Status,
    string SizeText,
    string NameOrPriceText,
    uint UnidentifiedValue,
    uint Tag1,
    uint Tag2,
    uint Tag3);

// WardCount is null when value 4 is missing or not a plausible count. TerritoryTypeId comes from the portal agent and
// is null when the agent was not readable.
internal sealed record WardReading(
    int WardIndex,
    string WardName,
    int? WardCount,
    uint BuyerFlags,
    uint? TerritoryTypeId,
    uint WorldId,
    ImmutableArray<WardPlotRow> Rows,
    WardPortalReading? Packet,
    long UpdateSequence,
    WardCorrelation Correlation,
    DateTime ReadAtUtc)
{
    public WardReading Acknowledged(WardPortalReading packet)
        => this with { Correlation = WardCorrelation.PacketAndAddon, Packet = packet };
}

// One plot of the ward packet. Flags are the game's HouseInfoFlags: 1 owned, 2 visitors allowed, 4 search comment,
// 8 house built, 16 owned by a Free Company. Appeals are HousingAppeal row IDs.
internal readonly record struct WardPortalEntry(int PlotIndex, uint Price, byte Flags, byte Appeal1, byte Appeal2, byte Appeal3)
{
    private const byte PlotOwnedFlag = 1;

    public bool PlotOwned => (Flags & PlotOwnedFlag) != 0;
}

// PurchaseType and TenantType are the two bytes after the entries; PaissaHouse reads them as the ward's purchase type
// (1 first come, 2 lottery) and tenant type (1 Free Company, 2 private).
internal sealed record WardPortalReading(
    uint TerritoryTypeId,
    int WardIndex,
    uint WorldId,
    ImmutableArray<WardPortalEntry> Entries,
    byte PurchaseType,
    byte TenantType,
    long Sequence,
    DateTime CapturedAtUtc);
