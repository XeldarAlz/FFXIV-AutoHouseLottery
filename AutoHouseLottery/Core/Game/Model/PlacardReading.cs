using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Game.Model;

internal enum PlacardSaleSource : byte
{
    None,
    SaleInfoHook,
    AgentSaleBlock,
}

// Agree: at least two sources could be compared and every comparison agreed. Disagree: some pair named different
// plots. Incomplete: fewer than two sources could be compared, so the address is not established.
internal enum AddressAgreement : byte
{
    Agree,
    Disagree,
    Incomplete,
}

// A plot address with zero-based ward and plot, as the game counts them.
internal readonly record struct PlotAddress(uint TerritoryTypeId, int WardIndex, int PlotIndex);

// A button of a housing window. EventParam is the value the button's own click event carries, logged so the lottery
// buttons' callback values can be recorded; MatchedText is null when its label matches no known row.
internal readonly record struct WindowButton(uint NodeId, string Text, bool Enabled, bool Visible, int EventParam, HousingText? MatchedText);

// Each source keeps its own address: the sale handler's capture for this window, the agent's fields and the window's
// address text. Address is set only when they agree. Every other field is null when no source supplied it.
internal sealed record PlacardReading(
    PlotAddress? HookAddress,
    PlotAddress? AgentAddress,
    string? AddressText,
    AddressAgreement Agreement,
    short? ApartmentNumber,
    PlacardHousingType? HousingType,
    PlacardSaleInfo? Sale,
    PlacardSaleSource SaleSource,
    bool? ForSale,
    byte? Size,
    ulong? OwnerFreeCompanyId,
    string? DetailsText,
    ImmutableArray<WindowButton> Buttons,
    long Sequence,
    DateTime ReadAtUtc)
{
    public PlotAddress? Address => Agreement == AddressAgreement.Agree ? HookAddress ?? AgentAddress : null;
}

// What the placard's sale handler received, kept until the next placard. Sequence orders it against the window's
// open and close.
internal readonly record struct PlacardSaleCapture(
    PlacardHousingType? HousingType,
    byte RawHousingType,
    PlotAddress Address,
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

internal static class PlacardSaleRule
{
    // The agent keeps the last sale block it was given. When this window's own capture exists, its housing type alone
    // decides: only an unowned house has a sale block. Without a capture, the agent's block counts only while the agent
    // says this plot is for sale.
    public static PlacardSaleSource Choose(PlacardSaleCapture? captureForWindow, bool? agentForSale, PlacardSaleInfo? agentSale, out PlacardSaleInfo? sale)
    {
        sale = null;
        if (captureForWindow is { } capture)
        {
            if (capture.HousingType != PlacardHousingType.UnownedHouse || capture.Sale is not { } captured)
            {
                return PlacardSaleSource.None;
            }

            sale = captured;
            return PlacardSaleSource.SaleInfoHook;
        }

        if (agentForSale != true || agentSale is not { } block)
        {
            return PlacardSaleSource.None;
        }

        sale = block;
        return PlacardSaleSource.AgentSaleBlock;
    }
}

internal static class PlacardAddressRule
{
    private const int DecimalBase = 10;

    // The window's text is compared without parsing its wording: it names the plot when it carries the district's name
    // in the client language and both the ward and the plot number as separate numbers.
    public static bool TextNames(string addressText, in PlotAddress address, string districtName)
    {
        var text = DisplayText.Normalize(addressText);
        var district = DisplayText.Normalize(districtName);
        return district.Length > 0
            && text.Contains(district, StringComparison.OrdinalIgnoreCase)
            && ContainsNumber(text, address.WardIndex + ResidentialDistricts.OneBased)
            && ContainsNumber(text, address.PlotIndex + ResidentialDistricts.OneBased);
    }

    // districtNameOf answers null when a territory's name is not known, which leaves the text out of the comparison.
    public static AddressAgreement Evaluate(PlotAddress? hook, PlotAddress? agent, string? addressText, Func<uint, string?> districtNameOf)
    {
        var comparisons = 0;
        var disagree = false;
        if (hook is { } hookAddress && agent is { } agentAddress)
        {
            comparisons++;
            disagree |= hookAddress != agentAddress;
        }

        if (addressText is not null && (hook ?? agent) is { } numbered && districtNameOf(numbered.TerritoryTypeId) is { } districtName)
        {
            comparisons++;
            disagree |= !TextNames(addressText, numbered, districtName);
        }

        if (disagree)
        {
            return AddressAgreement.Disagree;
        }

        return comparisons > 0 ? AddressAgreement.Agree : AddressAgreement.Incomplete;
    }

    private static bool ContainsNumber(string text, int number)
    {
        var index = 0;
        while (index < text.Length)
        {
            if (!char.IsAsciiDigit(text[index]))
            {
                index++;
                continue;
            }

            var value = 0;
            while (index < text.Length && char.IsAsciiDigit(text[index]))
            {
                value = unchecked((value * DecimalBase) + (text[index] - '0'));
                index++;
            }

            if (value == number)
            {
                return true;
            }
        }

        return false;
    }
}
