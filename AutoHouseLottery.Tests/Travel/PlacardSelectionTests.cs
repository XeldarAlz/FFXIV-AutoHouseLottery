using AutoHouseLottery.Core.Travel.Model;
using System.Numerics;

namespace AutoHouseLottery.Tests.Travel;

public sealed class PlacardSelectionTests
{
    private const float SearchMeters = 25f;

    private static readonly Vector3 entrance = new(100f, 0f, 100f);

    [Fact]
    public void PicksThePlacardNearestThePlotEntrance()
    {
        Vector3[] placards = [new(130f, 0f, 100f), new(104f, 0f, 103f), new(90f, 0f, 90f)];
        Assert.Equal(1, PlacardSelection.Nearest(placards, entrance, SearchMeters));
    }

    [Fact]
    public void IgnoresPlacardsBeyondTheSearchRadius()
    {
        Vector3[] placards = [new(126f, 0f, 100f), new(100f, 0f, 74f)];
        Assert.Equal(PlacardSelection.NoCandidate, PlacardSelection.Nearest(placards, entrance, SearchMeters));
    }

    [Fact]
    public void NoPlacardsMeansNoCandidate()
    {
        Assert.Equal(PlacardSelection.NoCandidate, PlacardSelection.Nearest([], entrance, SearchMeters));
    }
}
