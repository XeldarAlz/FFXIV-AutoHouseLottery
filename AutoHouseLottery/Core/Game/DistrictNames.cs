using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace AutoHouseLottery.Core.Game;

internal static class DistrictNames
{
    // The district's place name in the client language, as the placard's address text writes it; null when unknown.
    public static string? Of(uint territoryTypeId)
    {
        var name = Svc.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryTypeId)?.PlaceName.ValueNullable?.Name.ExtractText();
        return string.IsNullOrEmpty(name) ? null : name;
    }
}
