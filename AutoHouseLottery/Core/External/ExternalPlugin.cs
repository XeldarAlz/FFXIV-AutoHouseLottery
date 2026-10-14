using ECommons.DalamudServices;

namespace AutoHouseLottery.Core.External;

public enum ExternalPlugin
{
    Lifestream,
    Vnavmesh,
}

public sealed record ExternalPluginInfo(
    string InternalName,
    string DisplayName,
    string RepoUrl,
    string Purpose,
    bool Required,
    // Alternate InternalNames (community forks with the same IPC surface).
    string[]? Aliases = null);

// Dalamud copies its installed plugin list under a lock on every read, and the windows ask several times a frame, so
// the answer is read once a second at most. Framework thread only.
public static class ExternalPlugins
{
    private const long RefreshIntervalMs = 1_000;

    private static readonly ExternalPlugin[] all = [ExternalPlugin.Lifestream, ExternalPlugin.Vnavmesh];
    private static readonly bool[] loaded = new bool[all.Length];
    private static readonly Version?[] versions = new Version?[all.Length];
    private static long refreshedAtMs = long.MinValue;

    public static readonly IReadOnlyDictionary<ExternalPlugin, ExternalPluginInfo> Catalog
        = new Dictionary<ExternalPlugin, ExternalPluginInfo>
    {
        [ExternalPlugin.Lifestream] = new(
            InternalName: "Lifestream",
            DisplayName: "Lifestream",
            RepoUrl: "https://love.puni.sh/ment.json",
            Purpose: "Travels to a plot's world, ward and plot for entries, result checks and refunds.",
            Required: true),
        [ExternalPlugin.Vnavmesh] = new(
            InternalName: "vnavmesh",
            DisplayName: "vnavmesh",
            RepoUrl: "https://puni.sh/api/repository/veyn",
            Purpose: "Walks the last metres to a plot's placard. Without it, you walk up to the placard when the plugin asks.",
            Required: false),
    };

    public static IReadOnlyList<ExternalPlugin> All => all;

    public static bool IsInstalled(ExternalPlugin plugin)
    {
        Refresh();
        return loaded[(int)plugin];
    }

    // The loaded version, null while the plugin is not loaded.
    public static Version? LoadedVersion(ExternalPlugin plugin)
    {
        Refresh();
        return versions[(int)plugin];
    }

    private static void Refresh()
    {
        var now = Environment.TickCount64;
        if (refreshedAtMs != long.MinValue && now - refreshedAtMs < RefreshIntervalMs)
        {
            return;
        }

        refreshedAtMs = now;
        Array.Clear(loaded);
        Array.Clear(versions);
        foreach (var installed in Svc.PluginInterface.InstalledPlugins)
        {
            if (!installed.IsLoaded)
            {
                continue;
            }

            for (var index = 0; index < all.Length; index++)
            {
                if (Matches(Catalog[all[index]], installed.InternalName))
                {
                    loaded[(int)all[index]] = true;
                    versions[(int)all[index]] = installed.Version;
                }
            }
        }
    }

    private static bool Matches(ExternalPluginInfo info, string internalName)
        => internalName == info.InternalName || (info.Aliases is not null && Array.IndexOf(info.Aliases, internalName) >= 0);

    public static bool AllRequiredInstalled()
    {
        for (var index = 0; index < all.Length; index++)
        {
            if (Catalog[all[index]].Required && !IsInstalled(all[index]))
            {
                return false;
            }
        }

        return true;
    }

    public static string MissingRequiredNames()
    {
        var missing = new List<string>(all.Length);
        for (var index = 0; index < all.Length; index++)
        {
            var info = Catalog[all[index]];
            if (info.Required && !IsInstalled(all[index]))
            {
                missing.Add(info.DisplayName);
            }
        }

        return string.Join(", ", missing);
    }
}
