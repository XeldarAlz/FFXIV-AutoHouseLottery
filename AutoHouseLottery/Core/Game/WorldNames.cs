using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace AutoHouseLottery.Core.Game;

internal static class WorldNames
{
    // The world's name as the game's World sheet writes it, which is how Lifestream finds a character; null when unknown.
    public static string? Of(uint worldId)
    {
        var name = Svc.Data.GetExcelSheet<World>().GetRowOrDefault(worldId)?.Name.ExtractText();
        return string.IsNullOrEmpty(name) ? null : name;
    }
}
