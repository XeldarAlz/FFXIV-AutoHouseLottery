using System.Collections.Immutable;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Integration.Model;
using static AutoHouseLottery.Tests.HousingFixtures;
using LotteryPhase = AutoHouseLottery.Core.Housing.Model.LotteryPhase;

namespace AutoHouseLottery.Tests.Integration;

public sealed class ObservationMappingTests
{
    private static readonly PlotAddress address = new(HousingDistricts.Mist, WardIndex: 11, PlotIndex: 4);
    private static readonly DateTime phaseEnd = CycleStart + LotteryCalendar.EntryPeriod;
    private static readonly PlacardSaleInfo openLottery = new(PlacardPurchaseType.Lottery, PlacardTenantType.Private, PlacardAvailability.Available, phaseEnd, 7, 2, 2, 1);

    [Fact]
    public void PlacardWithoutAnAgreedAddressObservesNothing()
    {
        var disagreed = Placard(openLottery) with { AgentAddress = address with { PlotIndex = 9 }, Agreement = AddressAgreement.Disagree };

        Assert.Null(PlacardObservations.From(disagreed, HomeWorld, GameRegion.Global, FirstCycle));
    }

    [Fact]
    public void PlacardOpenForEntryRecordsItsSaleBuyerAndCountForTheVerifiedCycle()
    {
        var observation = PlacardObservations.From(Placard(openLottery), HomeWorld, GameRegion.Global, FirstCycle);

        Assert.NotNull(observation);
        var seen = observation.Value;
        Assert.Equal(Plot(12, 5), seen.Key);
        Assert.Equal(ObservationSource.Placard, seen.Provenance.Source);
        Assert.Equal(PlotSale.LotteryEntry, seen.Sale?.Sale);
        Assert.Equal(PlotBuyers.Private, seen.Buyers);
        Assert.Equal(7, seen.EntryCount);
        Assert.Equal(PlotOccupancy.Vacant, seen.Occupancy);
        Assert.Null(seen.Price);
        Assert.Equal(MergeRejection.None, PlotMerge.Validate(seen));
    }

    [Fact]
    public void WithoutAVerifiedCycleTheCycleBoundFieldsStayOut()
    {
        var seen = PlacardObservations.From(Placard(openLottery), HomeWorld, GameRegion.Global, null)!.Value;

        Assert.Null(seen.Sale);
        Assert.Null(seen.EntryCount);
        Assert.Equal(PlotBuyers.Private, seen.Buyers);
        Assert.Equal(MergeRejection.None, PlotMerge.Validate(seen));
    }

    [Fact]
    public void OwnedHouseIsOccupiedWithNoSale()
    {
        var owned = Placard(null) with { HousingType = PlacardHousingType.OwnedHouse };
        var seen = PlacardObservations.From(owned, HomeWorld, GameRegion.Global, FirstCycle)!.Value;

        Assert.Equal(PlotOccupancy.Occupied, seen.Occupancy);
        Assert.Null(seen.Sale);
        Assert.Equal(MergeRejection.None, PlotMerge.Validate(seen));
    }

    [Fact]
    public void SaleBlockMapsEachPurchaseModeApart()
    {
        Assert.Equal(PlotSale.LotteryResults, PlacardObservations.SaleOf(openLottery with { Availability = PlacardAvailability.InResultsPeriod })?.Sale);
        Assert.Equal(PlotSale.Unavailable, PlacardObservations.SaleOf(openLottery with { Availability = PlacardAvailability.Unavailable })?.Sale);
        Assert.Equal(PlotSale.FirstComeOpen, PlacardObservations.SaleOf(openLottery with { PurchaseType = PlacardPurchaseType.FirstComeFirstServed })?.Sale);
        Assert.Null(PlacardObservations.SaleOf(openLottery with { Availability = null }));
        Assert.Null(PlacardObservations.SaleOf(openLottery with { PurchaseType = null }));
    }

    [Fact]
    public void PhaseEndComesOnlyFromALotterySaleWithAnEndTime()
    {
        Assert.Equal(new PhaseEnd(LotteryPhase.Entry, phaseEnd), PlacardObservations.PhaseOf(openLottery, ObservedAt));
        Assert.Equal(LotteryPhase.Results, PlacardObservations.PhaseOf(openLottery with { Availability = PlacardAvailability.InResultsPeriod }, ObservedAt)?.Phase);
        Assert.Null(PlacardObservations.PhaseOf(openLottery with { PhaseEndsAtUtc = null }, ObservedAt));
        Assert.Null(PlacardObservations.PhaseOf(openLottery with { PurchaseType = PlacardPurchaseType.FirstComeFirstServed }, ObservedAt));
        Assert.Null(PlacardObservations.PhaseOf(openLottery with { Availability = PlacardAvailability.Unavailable }, ObservedAt));
    }

    [Fact]
    public void PhaseThatEndedBeforeTheReadingSaysNothingAboutTheCalendar()
    {
        Assert.Null(PlacardObservations.PhaseOf(openLottery, phaseEnd));
        Assert.Null(PlacardObservations.PhaseOf(openLottery, phaseEnd.AddMinutes(1)));
    }

    [Fact]
    public void AcknowledgedWardRecordsOwnershipPriceAndBuyersButNoLotteryPhase()
    {
        var packet = Packet(wardIndex: 11, purchaseType: 2, tenantType: 2, (4, 3_187_500u, 0), (5, 0u, 1));
        var batch = WardObservations.From(Scan(WardScanStop.Completed, 30, packet), GameRegion.Global, ObservedAt);

        Assert.NotNull(batch);
        var observations = batch.Value.Observations;
        Assert.Equal(2, observations.Length);
        var vacant = observations[0];
        Assert.Equal(Plot(12, 5), vacant.Key);
        Assert.Equal(PlotOccupancy.Vacant, vacant.Occupancy);
        Assert.Equal(3_187_500, vacant.Price);
        Assert.Equal(PlotBuyers.Private, vacant.Buyers);
        Assert.Null(vacant.Sale);
        Assert.Null(vacant.EntryCount);
        var owned = observations[1];
        Assert.Equal(PlotOccupancy.Occupied, owned.Occupancy);
        Assert.Null(owned.Price);
        for (var index = 0; index < observations.Length; index++)
        {
            Assert.Equal(MergeRejection.None, PlotMerge.Validate(observations[index]));
        }
    }

    [Fact]
    public void StoppedScanRecordsPartialCoverage()
    {
        var packet = Packet(wardIndex: 0, purchaseType: 1, tenantType: 1, (0, 1_000_000u, 0));

        var batch = WardObservations.From(Scan(WardScanStop.TimedOut, 30, packet), GameRegion.Global, ObservedAt)!.Value;

        Assert.Equal(CoverageOutcome.Partial, batch.Coverage.Outcome);
        Assert.Equal(PlotSale.FirstComeOpen, batch.Observations[0].Sale?.Sale);
        Assert.Equal(PlotBuyers.FreeCompany, batch.Observations[0].Buyers);
    }

    [Fact]
    public void ScanThatCrossedADistrictIsNotRecorded()
    {
        var first = Packet(wardIndex: 0, purchaseType: 2, tenantType: 2, (0, 1_000_000u, 0));
        var other = Packet(wardIndex: 1, purchaseType: 2, tenantType: 2, (0, 1_000_000u, 0)) with { TerritoryTypeId = HousingDistricts.Goblet };

        Assert.Null(WardObservations.From(Scan(WardScanStop.Completed, 2, first, other), GameRegion.Global, ObservedAt));
    }

    private static PlacardReading Placard(PlacardSaleInfo? sale) => new(
        address,
        address,
        null,
        AddressAgreement.Agree,
        null,
        sale is null ? null : PlacardHousingType.UnownedHouse,
        sale,
        sale is null ? PlacardSaleSource.None : PlacardSaleSource.SaleInfoHook,
        sale is not null,
        0,
        0,
        null,
        [],
        1,
        ObservedAt);

    private static WardPortalReading Packet(int wardIndex, byte purchaseType, byte tenantType, params (int Plot, uint Price, byte Flags)[] entries)
    {
        var builder = ImmutableArray.CreateBuilder<WardPortalEntry>(entries.Length);
        for (var index = 0; index < entries.Length; index++)
        {
            builder.Add(new WardPortalEntry(entries[index].Plot, entries[index].Price, entries[index].Flags, 0, 0, 0));
        }

        return new WardPortalReading(HousingDistricts.Mist, wardIndex, HomeWorld, builder.MoveToImmutable(), purchaseType, tenantType, 1, ObservedAt);
    }

    private static WardScanResult Scan(WardScanStop stop, int planned, params WardPortalReading[] packets)
    {
        var wards = ImmutableArray.CreateBuilder<WardReading>(packets.Length);
        for (var index = 0; index < packets.Length; index++)
        {
            wards.Add(new WardReading(packets[index].WardIndex, string.Empty, null, 0, packets[index].TerritoryTypeId, HomeWorld, [], packets[index], 1, WardCorrelation.PacketAndAddon, ObservedAt));
        }

        return new WardScanResult(wards.MoveToImmutable(), planned, stop, null);
    }
}
