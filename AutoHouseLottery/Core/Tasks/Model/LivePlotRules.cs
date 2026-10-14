using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Travel.Model;

namespace AutoHouseLottery.Core.Tasks.Model;

// What the open placard says about the target, before anything is pressed.
internal readonly record struct LivePlotFacts(
    PlacardIdentityVerdict Identity,
    PlacardHousingType? HousingType,
    PlacardSaleInfo? Sale,
    BuyerType IntendedBuyer,
    bool EnterButtonUsable,
    CycleKey FrozenCycle,
    CycleKey? LiveCycle);

internal static class LivePlotRules
{
    public static EntryFailure Check(in LivePlotFacts facts)
    {
        switch (facts.Identity)
        {
            case PlacardIdentityVerdict.WrongPlot:
                return EntryFailure.WrongPlacard;
            case PlacardIdentityVerdict.Unverified:
                return EntryFailure.IdentityUnverified;
        }

        if (facts.HousingType == PlacardHousingType.OwnedHouse)
        {
            return EntryFailure.Sold;
        }

        if (facts.Sale is not { } sale)
        {
            return facts.HousingType is null ? EntryFailure.SaleUnreadable : EntryFailure.Sold;
        }

        var saleFailure = CheckSale(sale, facts.IntendedBuyer);
        if (saleFailure != EntryFailure.None)
        {
            return saleFailure;
        }

        if (facts.LiveCycle is not { } live)
        {
            return EntryFailure.SaleUnreadable;
        }

        if (live != facts.FrozenCycle)
        {
            return EntryFailure.CycleChanged;
        }

        return facts.EnterButtonUsable ? EntryFailure.None : EntryFailure.EntryButtonUnavailable;
    }

    // A value outside the known set reads as unreadable, never as open for entry.
    private static EntryFailure CheckSale(in PlacardSaleInfo sale, BuyerType buyer)
    {
        switch (sale.PurchaseType)
        {
            case null:
                return EntryFailure.SaleUnreadable;
            case PlacardPurchaseType.Unavailable:
                return EntryFailure.Sold;
            case PlacardPurchaseType.FirstComeFirstServed:
                return EntryFailure.NotLottery;
        }

        switch (sale.Availability)
        {
            case null:
                return EntryFailure.SaleUnreadable;
            case PlacardAvailability.Unavailable:
                return EntryFailure.Sold;
            case PlacardAvailability.InResultsPeriod:
                return EntryFailure.WrongPhase;
        }

        return sale.TenantType switch
        {
            null => EntryFailure.SaleUnreadable,
            PlacardTenantType.Private when buyer == BuyerType.Private => EntryFailure.None,
            PlacardTenantType.FreeCompany when buyer == BuyerType.FreeCompany => EntryFailure.None,
            _ => EntryFailure.Ineligible,
        };
    }
}
