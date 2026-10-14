using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Numerics;
using System.Threading.Tasks;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Maps.Model;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace AutoHouseLottery.Core.Maps;

// Ported from Aetherphone's housing maps. A district's main and subdivision maps come from its TerritoryType map, the
// HousingMapMarkerInfo plot markers grouped by the map they reference, and the aethernet shard and market board icons of
// the MapMarker sheet. Sheets are read once per district on a task; the page shows the list until the map is ready.
internal sealed class HousingGameMaps
{
    public const uint AethernetShardIcon = 60430;
    public const uint MarketBoardIcon = 60570;

    private const byte AethernetShardDataType = 4;
    private const byte MarketBoardDataType = 0;

    private readonly IDataManager data;
    private readonly ConcurrentDictionary<ushort, DistrictMap> built = new();
    private readonly ConcurrentDictionary<ushort, byte> requested = new();

    public HousingGameMaps(IDataManager data)
    {
        this.data = data;
    }

    public DistrictMap? Get(ushort districtId)
    {
        if (built.TryGetValue(districtId, out var map))
        {
            return map;
        }

        if (requested.TryAdd(districtId, 0))
        {
            _ = Task.Run(() => built[districtId] = BuildSafely(districtId));
        }

        return null;
    }

    private DistrictMap BuildSafely(ushort districtId)
    {
        try
        {
            var map = Build(districtId);
            if (map.Failure == MapFailure.None)
            {
                RunLog.Info($"Loaded the game map {map.Main?.MapId} for district {districtId}: {map.Main?.Plots.Length} main and {map.Subdivision?.Plots.Length ?? 0} subdivision plot markers.");
            }
            else
            {
                RunLog.Warning($"The game map for district {districtId} is unavailable ({map.Failure}: {map.Detail}); the district is shown as a list.");
            }

            return map;
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, $"Reading the game map for district {districtId} failed");
            return new DistrictMap(districtId, null, null, MapFailure.Error, exception.Message);
        }
    }

    private DistrictMap Build(ushort districtId)
    {
        if (data.GetExcelSheet<TerritoryType>().GetRowOrDefault(districtId) is not { } territory)
        {
            return Failed(districtId, MapFailure.TerritoryMissing, $"no TerritoryType row {districtId}");
        }

        if (territory.Map.ValueNullable is not { } mainMap)
        {
            return Failed(districtId, MapFailure.MapMissing, $"TerritoryType {districtId} has no map");
        }

        var groups = MarkerGroups(districtId);
        if (!groups.TryGetValue(mainMap.RowId, out var mainMarkers))
        {
            return Failed(districtId, MapFailure.NoMarkers, $"no plot markers reference map {mainMap.RowId}");
        }

        var (main, failure, detail) = Division(mainMap, mainMarkers, 1);
        if (main is null)
        {
            return Failed(districtId, failure, detail);
        }

        DivisionMap? subdivision = null;
        if (SubdivisionGroup(groups, mainMap.RowId) is { } group && data.GetExcelSheet<Map>().GetRowOrDefault(group.Key) is { } subdivisionMap)
        {
            subdivision = Division(subdivisionMap, group.Value, HousingDistricts.PlotsPerDivision + 1).Map;
        }

        return new DistrictMap(districtId, main, subdivision, MapFailure.None, string.Empty);
    }

    private static DistrictMap Failed(ushort districtId, MapFailure failure, string detail) => new(districtId, null, null, failure, detail);

    private (DivisionMap? Map, MapFailure Failure, string Detail) Division(Map map, List<Vector3> markers, int firstPlot)
    {
        var mapId = map.Id.ExtractText();
        if (string.IsNullOrEmpty(mapId) || MapTextures.ResolveTexturePath(data, mapId) is not { } texture)
        {
            return (null, MapFailure.TextureMissing, $"no texture for map '{mapId}'");
        }

        if (markers.Count < HousingDistricts.PlotsPerDivision)
        {
            return (null, MapFailure.TooFewMarkers, $"map '{mapId}' has {markers.Count} of {HousingDistricts.PlotsPerDivision} plot markers");
        }

        var plots = MapProjection.Project(markers.ToArray(), map.OffsetX, map.OffsetY, map.SizeFactor, firstPlot);
        return plots.IsEmpty
            ? (null, MapFailure.ProjectionFailed, $"plot markers fall outside map '{mapId}'")
            : (new DivisionMap(mapId, texture, plots, PointsOfInterest(map)), MapFailure.None, string.Empty);
    }

    private Dictionary<uint, List<Vector3>> MarkerGroups(uint districtId)
    {
        var groups = new Dictionary<uint, List<Vector3>>();
        if (data.GetSubrowExcelSheet<HousingMapMarkerInfo>().GetRowOrDefault(districtId) is not { } row)
        {
            return groups;
        }

        foreach (var marker in row)
        {
            if (!groups.TryGetValue(marker.Map.RowId, out var list))
            {
                list = new List<Vector3>(HousingDistricts.PlotsPerDivision);
                groups[marker.Map.RowId] = list;
            }

            list.Add(new Vector3(marker.X, marker.Y, marker.Z));
        }

        return groups;
    }

    private static KeyValuePair<uint, List<Vector3>>? SubdivisionGroup(Dictionary<uint, List<Vector3>> groups, uint mainMapId)
    {
        KeyValuePair<uint, List<Vector3>>? best = null;
        foreach (var group in groups)
        {
            if (group.Key == mainMapId || group.Key == 0 || group.Value.Count < HousingDistricts.PlotsPerDivision)
            {
                continue;
            }

            if (best is not { } current || group.Value.Count > current.Value.Count)
            {
                best = group;
            }
        }

        return best;
    }

    private ImmutableArray<MapPointOfInterest> PointsOfInterest(Map map)
    {
        if (map.MapMarkerRange == 0 || data.GetSubrowExcelSheet<MapMarker>().GetRowOrDefault(map.MapMarkerRange) is not { } markers)
        {
            return [];
        }

        var points = ImmutableArray.CreateBuilder<MapPointOfInterest>();
        foreach (var marker in markers)
        {
            var isShard = marker.DataType == AethernetShardDataType && marker.Icon == AethernetShardIcon;
            var isMarketBoard = marker.DataType == MarketBoardDataType && marker.Icon == MarketBoardIcon;
            if (!isShard && !isMarketBoard)
            {
                continue;
            }

            var position = new Vector2(marker.X / MapProjection.PageSize, marker.Y / MapProjection.PageSize);
            if (position.X is >= 0f and <= 1f && position.Y is >= 0f and <= 1f)
            {
                points.Add(new MapPointOfInterest(marker.Icon, position));
            }
        }

        return points.ToImmutable();
    }
}
