using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Housing.Model;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests;

internal static class FindFixtures
{
    public static PlotSnapshot Listing(
        PlotKey key,
        PlotSale sale = PlotSale.LotteryEntry,
        int? entries = 3,
        long? price = Deposit,
        PlotSize? size = PlotSize.Small,
        PlotBuyers buyers = PlotBuyers.Both,
        DateTime? seen = null,
        CycleKey? cycle = null)
    {
        var provenance = Seen(ObservationSource.PublicFeed, seen ?? ObservedAt, cycle ?? FirstCycle);
        return new PlotSnapshot(key)
        {
            Occupancy = sale == PlotSale.NotReported ? null : new Observed<PlotOccupancy>(PlotOccupancy.Vacant, provenance),
            Sale = new Observed<SaleState>(new SaleState(sale, CycleStart + LotteryCalendar.EntryPeriod), provenance),
            Size = size is { } knownSize ? new Observed<PlotSize>(knownSize, provenance) : null,
            Price = price is { } knownPrice ? new Observed<long>(knownPrice, provenance) : null,
            Buyers = new Observed<PlotBuyers>(buyers, provenance),
            EntryCount = entries is { } count ? new Observed<int>(count, provenance) : null,
        };
    }

    public static Dictionary<PlotKey, PlotSnapshot> Plots(params PlotSnapshot[] snapshots)
    {
        var plots = new Dictionary<PlotKey, PlotSnapshot>();
        for (var index = 0; index < snapshots.Length; index++)
        {
            plots[snapshots[index].Key] = snapshots[index];
        }

        return plots;
    }

    public static AreaScope Scope(params uint[] districts) => new(HomeWorld, AreaScope.DistrictMaskOf(districts), true, 0, true, true);

    public static FindQuery Query(AreaScope area, PlotFilter? filter = null, ResultSort sort = ResultSort.Address)
    {
        return new FindQuery(area, filter ?? PlotFilter.Default, sort, 0, VerifiedEntryPhase(), ObservedAt.AddHours(1));
    }
}
