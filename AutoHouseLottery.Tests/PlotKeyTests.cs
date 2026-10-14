using AutoHouseLottery.Core.Housing.Model;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests;

public sealed class PlotKeyTests
{
    [Fact]
    public void EveryDistrictCarriesItsWardAndPlotCountsAsData()
    {
        uint[] territories = [339, 340, 341, 641, 979];
        for (var index = 0; index < territories.Length; index++)
        {
            Assert.True(HousingDistricts.TryGet(territories[index], out var district));
            Assert.Equal(30, district.WardCount);
            Assert.Equal(60, district.PlotsPerWard);
        }

        Assert.Equal(territories.Length, HousingDistricts.All.Length);
    }

    [Fact]
    public void PlotsThirtyOneToSixtyAreTheSubdivision()
    {
        Assert.False(Plot(1, 30).IsSubdivision);
        Assert.True(Plot(1, 31).IsSubdivision);
        Assert.True(Plot(30, 60, district: HousingDistricts.Empyreum).IsSubdivision);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(31, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 61)]
    [InlineData(-1, 5)]
    public void OutOfRangeAddressesAreRejectedNotClamped(int ward, int plot)
    {
        Assert.False(PlotKey.TryCreate(GameRegion.Global, HomeWorld, HousingDistricts.Mist, ward, plot, out var key));
        Assert.False(key.IsValid);
    }

    [Fact]
    public void UnknownDistrictWorldOrRegionIsRejected()
    {
        Assert.False(PlotKey.TryCreate(GameRegion.Global, HomeWorld, 342, 1, 1, out _));
        Assert.False(PlotKey.TryCreate(GameRegion.Global, 0, HousingDistricts.Mist, 1, 1, out _));
        Assert.False(PlotKey.TryCreate(GameRegion.Unknown, HomeWorld, HousingDistricts.Mist, 1, 1, out _));
    }

    [Fact]
    public void SameAddressOnTwoWorldsIsTwoPlots()
    {
        var home = Plot(12, 5);
        var other = Plot(12, 5, world: OtherWorld);

        Assert.NotEqual(home, other);
        Assert.Equal(2, new HashSet<PlotKey> { home, other, Plot(12, 5) }.Count);
    }

    [Fact]
    public void SameNumericIdsInTwoRegionsStayDistinct()
    {
        Assert.NotEqual(Plot(3, 3), Plot(3, 3, region: GameRegion.China));
    }

    [Fact]
    public void AddressOrderIsWorldThenDistrictThenWardThenPlot()
    {
        PlotKey[] keys =
        [
            Plot(2, 1, world: OtherWorld),
            Plot(1, 2, district: HousingDistricts.Goblet),
            Plot(2, 1),
            Plot(1, 40),
            Plot(1, 2),
        ];
        Array.Sort(keys);

        Assert.Equal([Plot(1, 2), Plot(1, 40), Plot(2, 1), Plot(1, 2, district: HousingDistricts.Goblet), Plot(2, 1, world: OtherWorld)], keys);
    }
}
