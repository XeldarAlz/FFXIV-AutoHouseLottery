using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Maps.Model;

internal sealed record DivisionMap(string MapId, string TexturePath, ImmutableArray<MapPlotPoint> Plots, ImmutableArray<MapPointOfInterest> PointsOfInterest);
