using AutoHouseLottery.Core.Housing.Model;
using ECommons.DalamudServices;
using WorldSheet = Lumina.Excel.Sheets.World;

namespace AutoHouseLottery.Core.Game;

// The region of a world from the game's World and WorldDCGroupType sheets, read once per world. Framework thread only.
internal static class WorldRegionReader
{
    private static readonly Dictionary<uint, WorldRegion> regions = [];

    public static WorldRegion Of(uint worldId)
    {
        if (worldId == 0)
        {
            return WorldRegion.Unknown;
        }

        if (regions.TryGetValue(worldId, out var known))
        {
            return known;
        }

        var region = WorldRegion.Unknown;
        try
        {
            if (Svc.Data.GetExcelSheet<WorldSheet>().TryGetRow(worldId, out var world) && world.DataCenter.RowId != 0)
            {
                region = WorldRegions.FromDataCenterRegion(world.DataCenter.Value.Region.RowId);
            }
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, $"The region of world {worldId} could not be read from the game data; housing there is refused");
        }

        regions[worldId] = region;
        if (!WorldRegions.Supported(region))
        {
            RunLog.Info($"World {worldId} is in region {region}; housing on it is not supported.");
        }

        return region;
    }

    public static bool Supported(uint worldId) => WorldRegions.Supported(Of(worldId));
}
