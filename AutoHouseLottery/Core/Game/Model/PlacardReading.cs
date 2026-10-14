using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Game.Model;

[Flags]
internal enum PlacardSources : byte
{
    None = 0,
    AddonText = 1,
    AgentFields = 2,
    SaleInfoHook = 4,
    AgentSaleBlock = 8,
}

// A button of the placard window. EventParam is the value the button's own click event carries, logged so the lottery
// buttons' callback values can be recorded; MatchedText is null when its label matches no known row.
internal readonly record struct PlacardButton(uint NodeId, string Text, bool Enabled, int EventParam, HousingText? MatchedText);

// Every field is null when no source could supply it. IdentitySource and SaleSource name where the address and the
// sale information came from; they agree with each other only when a test in game says so.
internal sealed record PlacardReading(
    uint? TerritoryTypeId,
    int? WardIndex,
    int? PlotIndex,
    short? ApartmentNumber,
    PlacardHousingType? HousingType,
    PlacardSaleInfo? Sale,
    bool? ForSale,
    byte? Size,
    ulong? OwnerFreeCompanyId,
    string? AddressText,
    string? DetailsText,
    ImmutableArray<PlacardButton> Buttons,
    PlacardSources IdentitySource,
    PlacardSources SaleSource,
    long Sequence,
    DateTime ReadAtUtc);

// What the placard's sale handler received, kept until the next placard. Sequence orders it against the window's
// open and close.
internal readonly record struct PlacardSaleCapture(
    PlacardHousingType? HousingType,
    byte RawHousingType,
    uint TerritoryTypeId,
    int WardIndex,
    int PlotIndex,
    short ApartmentNumber,
    PlacardSaleInfo? Sale,
    long Sequence);

internal static class PlacardCaptureRule
{
    // The sale handler runs as the placard opens, so a capture belongs to the open window when it came after the
    // previous window closed.
    public static bool BelongsToOpenWindow(long captureSequence, long lastClosedSequence, bool windowOpen)
        => windowOpen && captureSequence > lastClosedSequence;
}
