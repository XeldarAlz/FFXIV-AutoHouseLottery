using System.Collections.Immutable;
using System.Numerics;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Maps.Model;

// Projects HousingMapMarkerInfo world positions onto the district map texture as in Aetherphone: the game map page is
// 2048 units square around the map offset, scaled by its size factor. The sheet does not say whether the plot plane is
// X/Z or X/Y, so both are tried and the one that spreads the plots over the texture wins. A marker well off the page is
// left out rather than pinned to the edge, and markers mostly off the page mean the projection is wrong and no map is
// drawn at all.
internal static class MapProjection
{
    public const float PageSize = 2048f;

    private const float PercentScale = 100f;
    private const float OutsideTolerance = 0.08f;
    private const float OutsideAllowance = 0.15f;
    private const float MinimumSpread = 0.004f;
    private const int AxisAttempts = 2;

    public static float ScaleFactor(ushort sizeFactor) => sizeFactor == 0 ? 1f : sizeFactor / PercentScale;

    public static float Normalize(float world, short offset, float scaleFactor) => ((world + offset) * scaleFactor + PageSize * 0.5f) / PageSize;

    public static ImmutableArray<MapPlotPoint> Project(ReadOnlySpan<Vector3> markers, short offsetX, short offsetY, ushort sizeFactor, int firstPlot)
    {
        var scale = ScaleFactor(sizeFactor);
        var count = Math.Min(markers.Length, HousingDistricts.PlotsPerDivision);
        var best = ImmutableArray<MapPlotPoint>.Empty;
        var bestSpread = 0f;
        for (var attempt = 0; attempt < AxisAttempts; attempt++)
        {
            var candidate = ImmutableArray.CreateBuilder<MapPlotPoint>(count);
            var minimum = Vector2.One;
            var maximum = Vector2.Zero;
            var outside = 0;
            for (var index = 0; index < count; index++)
            {
                var world = markers[index];
                var vertical = attempt == 0 ? world.Z : world.Y;
                var point = new Vector2(Normalize(world.X, offsetX, scale), Normalize(vertical, offsetY, scale));
                if (point.X < -OutsideTolerance || point.X > 1f + OutsideTolerance || point.Y < -OutsideTolerance || point.Y > 1f + OutsideTolerance)
                {
                    outside++;
                    continue;
                }

                point = Vector2.Clamp(point, Vector2.Zero, Vector2.One);
                minimum = Vector2.Min(minimum, point);
                maximum = Vector2.Max(maximum, point);
                candidate.Add(new MapPlotPoint(firstPlot + index, point));
            }

            if (outside > count * OutsideAllowance)
            {
                continue;
            }

            var extent = maximum - minimum;
            var spread = extent.X * extent.Y;
            if (spread <= bestSpread)
            {
                continue;
            }

            bestSpread = spread;
            best = candidate.ToImmutable();
        }

        return bestSpread < MinimumSpread ? [] : best;
    }
}
