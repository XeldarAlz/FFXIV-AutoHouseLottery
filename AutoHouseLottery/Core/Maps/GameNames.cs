using System.Collections.Frozen;
using System.Globalization;
using System.Threading.Tasks;
using AutoHouseLottery.Core.Housing.Model;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace AutoHouseLottery.Core.Maps;

// World and district names in the client's language, read from the game's sheets once on a task. Until they arrive the
// page shows the numeric ID, so a draw never waits on a sheet.
internal sealed class GameNames
{
    private volatile FrozenDictionary<uint, string> worlds = FrozenDictionary<uint, string>.Empty;
    private volatile FrozenDictionary<uint, string> districts = FrozenDictionary<uint, string>.Empty;
    private volatile bool ready;

    public GameNames(IDataManager data)
    {
        _ = Task.Run(() => Load(data));
    }

    public bool Ready => ready;

    public string World(uint worldId)
    {
        return worlds.TryGetValue(worldId, out var name) ? name : worldId.ToString(CultureInfo.InvariantCulture);
    }

    public string District(uint districtId)
    {
        return districts.TryGetValue(districtId, out var name) ? name : districtId.ToString(CultureInfo.InvariantCulture);
    }

    private void Load(IDataManager data)
    {
        try
        {
            var worldNames = new Dictionary<uint, string>();
            foreach (var world in data.GetExcelSheet<Lumina.Excel.Sheets.World>())
            {
                var name = world.Name.ExtractText();
                if (name.Length > 0)
                {
                    worldNames[world.RowId] = name;
                }
            }

            var districtNames = new Dictionary<uint, string>();
            var territories = data.GetExcelSheet<TerritoryType>();
            for (var index = 0; index < HousingDistricts.All.Length; index++)
            {
                var districtId = HousingDistricts.All[index].TerritoryId;
                if (territories.GetRowOrDefault(districtId) is { } territory && territory.PlaceName.ValueNullable is { } place)
                {
                    districtNames[districtId] = place.Name.ExtractText();
                }
            }

            worlds = worldNames.ToFrozenDictionary();
            districts = districtNames.ToFrozenDictionary();
            ready = true;
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, "World and district names could not be read from the game data; IDs are shown instead");
        }
    }
}
