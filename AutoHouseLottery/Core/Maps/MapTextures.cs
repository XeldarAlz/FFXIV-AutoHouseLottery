using Dalamud.Plugin.Services;

namespace AutoHouseLottery.Core.Maps;

// Ported from Aetherphone: the game names map textures after the map ID in a few patterns, so each is tried in turn.
internal static class MapTextures
{
    public static string[] Candidates(string mapId)
    {
        var flat = mapId.Replace("/", string.Empty, StringComparison.Ordinal);
        var underscored = mapId.Replace('/', '_');
        return
        [
            $"ui/map/{mapId}/{flat}_m.tex",
            $"ui/map/{mapId}/{flat}m_m.tex",
            $"ui/map/{mapId}/{flat}_s.tex",
            $"ui/map/{mapId}/{underscored}_m.tex",
            $"ui/map/{mapId}/{underscored}m_m.tex",
        ];
    }

    public static string? ResolveTexturePath(IDataManager data, string mapId)
    {
        var candidates = Candidates(mapId);
        for (var index = 0; index < candidates.Length; index++)
        {
            if (data.FileExists(candidates[index]))
            {
                return candidates[index];
            }
        }

        return null;
    }
}
