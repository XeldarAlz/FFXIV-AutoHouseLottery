using System.Collections.Immutable;
using System.Numerics;

namespace AutoHouseLottery.Core.Maps.Model;

internal sealed record DivisionMap(string MapId, string TexturePath, ImmutableArray<MapPlotPoint> Plots, ImmutableArray<MapPointOfInterest> PointsOfInterest)
{
    public bool TryGetPoint(int plot, out Vector2 position)
    {
        for (var index = 0; index < Plots.Length; index++)
        {
            if (Plots[index].Plot == plot)
            {
                position = Plots[index].Position;
                return true;
            }
        }

        position = default;
        return false;
    }
}
