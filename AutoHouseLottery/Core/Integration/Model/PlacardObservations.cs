using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Housing.Model;
using LotteryPhase = AutoHouseLottery.Core.Housing.Model.LotteryPhase;

namespace AutoHouseLottery.Core.Integration.Model;

internal readonly record struct PhaseEnd(LotteryPhase Phase, DateTime EndsAtUtc);

internal static class PlacardObservations
{
    private const int OneBased = 1;
    private const byte SmallSize = 0;
    private const byte MediumSize = 1;
    private const byte LargeSize = 2;

    // The phase the placard's sale block reports and when it ends, which is server data the calendar can be anchored on.
    public static PhaseEnd? PhaseOf(PlacardSaleInfo? sale)
    {
        if (sale is not { PurchaseType: PlacardPurchaseType.Lottery, PhaseEndsAtUtc: { } endsAt } lottery)
        {
            return null;
        }

        return lottery.Availability switch
        {
            PlacardAvailability.Available => new PhaseEnd(LotteryPhase.Entry, endsAt),
            PlacardAvailability.InResultsPeriod => new PhaseEnd(LotteryPhase.Results, endsAt),
            _ => null,
        };
    }

    // A placard observation exists only for an address the placard's sources agree on. Fields the placard did not
    // supply stay unset, and the cycle-bound ones (sale, entry count) only travel with a verified cycle.
    public static PlotObservation? From(PlacardReading reading, uint worldId, GameRegion region, CycleKey? verifiedCycle)
    {
        if (reading.Address is not { } address
            || !PlotKey.TryCreate(region, worldId, address.TerritoryTypeId, address.WardIndex + OneBased, address.PlotIndex + OneBased, out var key))
        {
            return null;
        }

        var provenance = new Provenance(ObservationSource.Placard, reading.ReadAtUtc, reading.ReadAtUtc, verifiedCycle);
        var sale = verifiedCycle is null ? null : SaleOf(reading.Sale);
        return new PlotObservation(key, provenance)
        {
            Occupancy = OccupancyOf(reading),
            Sale = sale,
            Size = SizeOf(reading.Size),
            Buyers = BuyersOf(reading.Sale),
            EntryCount = verifiedCycle is not null && reading.Sale is { PurchaseType: PlacardPurchaseType.Lottery } lottery && lottery.EntryCount <= int.MaxValue
                ? (int)lottery.EntryCount
                : null,
        };
    }

    public static SaleState? SaleOf(PlacardSaleInfo? sale)
    {
        if (sale is not { PurchaseType: { } purchase } info)
        {
            return null;
        }

        var state = purchase switch
        {
            PlacardPurchaseType.Unavailable => PlotSale.Unavailable,
            PlacardPurchaseType.FirstComeFirstServed => info.Availability == PlacardAvailability.Available ? PlotSale.FirstComeOpen : PlotSale.Unavailable,
            _ => info.Availability switch
            {
                PlacardAvailability.Available => PlotSale.LotteryEntry,
                PlacardAvailability.InResultsPeriod => PlotSale.LotteryResults,
                PlacardAvailability.Unavailable => PlotSale.Unavailable,
                _ => PlotSale.Unknown,
            },
        };
        return state == PlotSale.Unknown ? null : new SaleState(state, info.PhaseEndsAtUtc);
    }

    public static PlotBuyers? BuyersOf(PlacardSaleInfo? sale) => sale?.TenantType switch
    {
        PlacardTenantType.Private => PlotBuyers.Private,
        PlacardTenantType.FreeCompany => PlotBuyers.FreeCompany,
        _ => null,
    };

    private static PlotOccupancy? OccupancyOf(PlacardReading reading) => reading.HousingType switch
    {
        PlacardHousingType.UnownedHouse => PlotOccupancy.Vacant,
        PlacardHousingType.OwnedHouse => PlotOccupancy.Occupied,
        _ => reading.ForSale == true ? PlotOccupancy.Vacant : null,
    };

    private static PlotSize? SizeOf(byte? size) => size switch
    {
        SmallSize => PlotSize.Small,
        MediumSize => PlotSize.Medium,
        LargeSize => PlotSize.Large,
        _ => null,
    };
}
