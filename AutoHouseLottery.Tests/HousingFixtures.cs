using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Tests;

internal static class HousingFixtures
{
    public const uint HomeWorld = 63;
    public const uint OtherWorld = 79;
    public const long Deposit = 3_187_500;

    public static readonly DateTime CycleStart = new(2026, 10, 14, 15, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime ObservedAt = CycleStart.AddHours(6);
    public static readonly CycleKey FirstCycle = new(GameRegion.Global, 1);
    public static readonly CycleKey SecondCycle = new(GameRegion.Global, 2);

    public static PlotKey Plot(int ward, int plot, uint world = HomeWorld, uint district = HousingDistricts.Mist, GameRegion region = GameRegion.Global)
    {
        Assert.True(PlotKey.TryCreate(region, world, district, ward, plot, out var key));
        return key;
    }

    public static CyclePosition VerifiedEntryPhase() => new(
        FirstCycle,
        LotteryPhase.Entry,
        CycleStart,
        CycleStart + LotteryCalendar.EntryPeriod,
        1,
        CycleEvidence.ServerObserved);

    public static Provenance Seen(ObservationSource source, DateTime sourceTimeUtc, CycleKey? cycle = null)
    {
        return new Provenance(source, sourceTimeUtc, sourceTimeUtc, cycle ?? FirstCycle);
    }

    public static PlotSnapshot ListedPlot(PlotKey key, int? reportedEntries, long price = Deposit, PlotBuyers buyers = PlotBuyers.Both)
    {
        var provenance = Seen(ObservationSource.PublicFeed, ObservedAt);
        return new PlotSnapshot(key)
        {
            Occupancy = new Observed<PlotOccupancy>(PlotOccupancy.Vacant, provenance),
            Sale = new Observed<SaleState>(new SaleState(PlotSale.LotteryEntry, CycleStart + LotteryCalendar.EntryPeriod), provenance),
            Price = new Observed<long>(price, provenance),
            Buyers = new Observed<PlotBuyers>(buyers, provenance),
            EntryCount = reportedEntries is { } count ? new Observed<int>(count, provenance) : null,
        };
    }
}
