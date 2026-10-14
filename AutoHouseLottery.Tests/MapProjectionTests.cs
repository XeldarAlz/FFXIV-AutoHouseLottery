using System.Numerics;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Maps.Model;

namespace AutoHouseLottery.Tests;

public sealed class MapProjectionTests
{
    private const ushort FullSize = 100;
    private const int SubdivisionFirstPlot = HousingDistricts.PlotsPerDivision + 1;

    [Fact]
    public void MarkersOnTheGroundPlaneProjectInsideTheTextureInPlotOrder()
    {
        var markers = Grid(height: 40f, verticalOnZ: true);

        var points = MapProjection.Project(markers, 0, 0, FullSize, SubdivisionFirstPlot);

        Assert.Equal(HousingDistricts.PlotsPerDivision, points.Length);
        Assert.Equal(SubdivisionFirstPlot, points[0].Plot);
        Assert.Equal(HousingDistricts.PlotsPerDivision * 2, points[^1].Plot);
        Assert.All(points, point => Assert.InRange(point.Position.X, 0f, 1f));
        Assert.Equal(MapProjection.Normalize(markers[0].Z, 0, 1f), points[0].Position.Y);
    }

    [Fact]
    public void TheVerticalAxisIsChosenByHowWellThePlotsSpread()
    {
        var points = MapProjection.Project(Grid(height: 40f, verticalOnZ: false), 0, 0, FullSize, 1);

        Assert.True(points[^1].Position.Y - points[0].Position.Y > 0.1f);
    }

    [Fact]
    public void MarkersMostlyOffTheMapGiveNoMapRatherThanWrongMarkers()
    {
        var markers = new Vector3[HousingDistricts.PlotsPerDivision];
        for (var index = 0; index < markers.Length; index++)
        {
            markers[index] = new Vector3(5_000f + index, 0f, 5_000f + index);
        }

        Assert.Empty(MapProjection.Project(markers, 0, 0, FullSize, 1));
    }

    private static Vector3[] Grid(float height, bool verticalOnZ)
    {
        var markers = new Vector3[HousingDistricts.PlotsPerDivision];
        for (var index = 0; index < markers.Length; index++)
        {
            var across = -600f + index % 6 * 200f;
            var down = -500f + index / 6 * 200f;
            markers[index] = verticalOnZ ? new Vector3(across, height, down) : new Vector3(across, down, height);
        }

        return markers;
    }
}
