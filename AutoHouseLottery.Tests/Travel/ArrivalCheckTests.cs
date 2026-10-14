using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Travel.Model;

namespace AutoHouseLottery.Tests.Travel;

public sealed class ArrivalCheckTests
{
    private const uint World = 73;
    private const uint Mist = 339;
    private const uint Goblet = 341;
    private const int Ward = 7;
    private const int MainPlot = 12;
    private const int SubdivisionPlot = 42;

    private static readonly HousingDestination mainPlot = new(World, Mist, Ward, MainPlot);
    private static readonly HousingDestination subdivisionPlot = new(World, Mist, Ward, SubdivisionPlot);

    [Fact]
    public void ArrivesWhenWorldDistrictWardAndDivisionAgree()
    {
        Assert.Equal(ArrivalVerdict.Arrived, ArrivalCheck.Evaluate(mainPlot, Observed(Mist, Ward, division: 1)));
        Assert.Equal(ArrivalVerdict.Arrived, ArrivalCheck.Evaluate(subdivisionPlot, Observed(Mist, Ward, division: 2)));
    }

    [Fact]
    public void NothingCountsWhileTheZoneIsLoading()
    {
        var loading = Observed(Mist, Ward, division: 1) with { ZoneReady = false };
        Assert.Equal(ArrivalVerdict.Unknown, ArrivalCheck.Evaluate(mainPlot, loading));
    }

    [Fact]
    public void AMissingWardOrDivisionIsNotTakenAsArrival()
    {
        Assert.Equal(ArrivalVerdict.Unknown, ArrivalCheck.Evaluate(mainPlot, Observed(Mist, ward: null, division: 1)));
        Assert.Equal(ArrivalVerdict.Unknown, ArrivalCheck.Evaluate(mainPlot, Observed(Mist, Ward, division: null)));
    }

    [Fact]
    public void NamesTheFirstThingThatDisagrees()
    {
        Assert.Equal(ArrivalVerdict.WrongWorld, ArrivalCheck.Evaluate(mainPlot, Observed(Mist, Ward, division: 1) with { WorldId = World + 1 }));
        Assert.Equal(ArrivalVerdict.WrongDistrict, ArrivalCheck.Evaluate(mainPlot, Observed(Goblet, Ward, division: 1)));
        Assert.Equal(ArrivalVerdict.WrongWard, ArrivalCheck.Evaluate(mainPlot, Observed(Mist, Ward + 1, division: 1)));
        Assert.Equal(ArrivalVerdict.WrongDivision, ArrivalCheck.Evaluate(subdivisionPlot, Observed(Mist, Ward, division: 1)));
    }

    [Fact]
    public void ThePlotItselfIsReportedOnlyWhenTheGameNamesOne()
    {
        Assert.Null(ArrivalCheck.OnTargetPlot(mainPlot, Observed(Mist, Ward, division: 1)));
        Assert.True(ArrivalCheck.OnTargetPlot(mainPlot, Observed(Mist, Ward, division: 1, plot: MainPlot)));
        Assert.False(ArrivalCheck.OnTargetPlot(mainPlot, Observed(Mist, Ward, division: 1, plot: MainPlot + 1)));
    }

    [Theory]
    [InlineData(29, 1)]
    [InlineData(30, 2)]
    [InlineData(0, 1)]
    [InlineData(59, 2)]
    public void PlotsThirtyOneToSixtyAreInTheSubdivision(int plotIndex, int expectedDivision)
    {
        Assert.Equal(expectedDivision, new HousingDestination(World, Mist, Ward, plotIndex).Division);
    }

    [Theory]
    [InlineData(World, Mist, 0, 0, true)]
    [InlineData(World, Mist, 29, 59, true)]
    [InlineData(0u, Mist, 0, 0, false)]
    [InlineData(World, 129u, 0, 0, false)]
    [InlineData(World, Mist, 30, 0, false)]
    [InlineData(World, Mist, 0, 60, false)]
    [InlineData(World, Mist, -1, 0, false)]
    public void OnlyARealPlotIsAValidDestination(uint worldId, uint territoryTypeId, int wardIndex, int plotIndex, bool expected)
    {
        Assert.Equal(expected, new HousingDestination(worldId, territoryTypeId, wardIndex, plotIndex).IsValid);
    }

    [Fact]
    public void TheGamesNoneValuesBecomeUnknown()
    {
        var location = HousingLocation.FromRaw(Mist, wardIndex: -1, plotIndex: -1, division: 0, inside: false);
        Assert.Null(location.WardIndex);
        Assert.Null(location.PlotIndex);
        Assert.Null(location.Division);
        Assert.Equal(new HousingLocation(Mist, 3, 0, 2, true), HousingLocation.FromRaw(Mist, 3, 0, 2, true));
    }

    [Theory]
    [InlineData("mist", 339u)]
    [InlineData("LAVENDER", 340u)]
    [InlineData("Shirogane", 641u)]
    public void DistrictKeywordsAreCaseInsensitive(string keyword, uint territoryTypeId)
    {
        Assert.True(ResidentialDistricts.TryFindByKeyword(keyword, out var district));
        Assert.Equal(territoryTypeId, district.TerritoryTypeId);
    }

    private static ArrivalObservation Observed(uint territoryTypeId, int? ward, int? division, int? plot = null)
        => new(true, World, new HousingLocation(territoryTypeId, ward, plot, division, false));
}
