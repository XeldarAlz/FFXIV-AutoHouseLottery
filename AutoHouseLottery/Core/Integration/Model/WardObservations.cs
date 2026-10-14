using System.Collections.Immutable;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Integration.Model;

internal readonly record struct WardBatch(CoverageSnapshot Coverage, ImmutableArray<PlotObservation> Observations);

internal static class WardObservations
{
    private const int OneBased = 1;
    private const byte FirstComePurchase = 1;
    private const byte FreeCompanyTenant = 1;
    private const byte PrivateTenant = 2;

    // Only wards acknowledged through their own packet count. The packet says who owns each plot, the price of the
    // unowned ones and the ward's buyer type; it says nothing about the lottery phase, so no lottery sale is recorded
    // and no entry count is touched.
    public static WardBatch? From(WardScanResult result, GameRegion region, DateTime receivedUtc)
    {
        if (result.Wards.IsDefaultOrEmpty || result.Wards[0].Packet is not { } first)
        {
            return null;
        }

        if (!CoverageScope.TryForDistrict(region, first.WorldId, first.TerritoryTypeId, out var scope))
        {
            return null;
        }

        var observations = ImmutableArray.CreateBuilder<PlotObservation>();
        var observedFrom = first.CapturedAtUtc;
        var observedUntil = first.CapturedAtUtc;
        for (var wardIndex = 0; wardIndex < result.Wards.Length; wardIndex++)
        {
            if (result.Wards[wardIndex].Packet is not { } packet || packet.WorldId != first.WorldId || packet.TerritoryTypeId != first.TerritoryTypeId)
            {
                return null;
            }

            observedFrom = packet.CapturedAtUtc < observedFrom ? packet.CapturedAtUtc : observedFrom;
            observedUntil = packet.CapturedAtUtc > observedUntil ? packet.CapturedAtUtc : observedUntil;
            AddWard(observations, packet, region, receivedUtc);
        }

        var outcome = result.Complete ? CoverageOutcome.Complete : CoverageOutcome.Partial;
        var coverage = new CoverageSnapshot(
            scope,
            ObservationSource.WardScan,
            outcome,
            result.WardsPlanned * ResidentialDistricts.PlotsPerWard,
            observations.Count,
            observedFrom,
            observedUntil,
            receivedUtc);
        return new WardBatch(coverage, observations.ToImmutable());
    }

    private static void AddWard(ImmutableArray<PlotObservation>.Builder observations, WardPortalReading packet, GameRegion region, DateTime receivedUtc)
    {
        var provenance = new Provenance(ObservationSource.WardScan, packet.CapturedAtUtc, receivedUtc, null);
        var buyers = packet.TenantType switch
        {
            FreeCompanyTenant => PlotBuyers.FreeCompany,
            PrivateTenant => PlotBuyers.Private,
            _ => (PlotBuyers?)null,
        };
        for (var entryIndex = 0; entryIndex < packet.Entries.Length; entryIndex++)
        {
            var entry = packet.Entries[entryIndex];
            if (!PlotKey.TryCreate(region, packet.WorldId, packet.TerritoryTypeId, packet.WardIndex + OneBased, entry.PlotIndex + OneBased, out var key))
            {
                continue;
            }

            var vacant = !entry.PlotOwned;
            observations.Add(new PlotObservation(key, provenance)
            {
                Occupancy = vacant ? PlotOccupancy.Vacant : PlotOccupancy.Occupied,
                Price = vacant && entry.Price > 0 ? entry.Price : null,
                Buyers = vacant ? buyers : null,
                Sale = vacant && packet.PurchaseType == FirstComePurchase ? new SaleState(PlotSale.FirstComeOpen, null) : null,
            });
        }
    }
}
