using System.Runtime.InteropServices;
using AutoHouseLottery.Core.Tasks.Model;
using ECommons.DalamudServices;

namespace AutoHouseLottery.Core.External;

// The shared start blocker for anything that opens a committing lottery dialog: entry runs, settlement visits and the
// probe's Enter Lottery and View Lottery Results presses. Dalamud copies its installed plugin list on every read, so it
// is read once a second at most. Framework thread only.
internal static class AutoAnswerGuard
{
    private const long RefreshIntervalMs = 1_000;

    private static readonly List<InstalledPlugin> installed = [];
    private static long refreshedAtMs = long.MinValue;
    private static string? loaded;

    // The display name of a loaded plugin that answers dialogs on its own, null when none is loaded.
    public static string? LoadedName()
    {
        var now = Environment.TickCount64;
        if (refreshedAtMs != long.MinValue && now - refreshedAtMs < RefreshIntervalMs)
        {
            return loaded;
        }

        refreshedAtMs = now;
        installed.Clear();
        foreach (var plugin in Svc.PluginInterface.InstalledPlugins)
        {
            installed.Add(new InstalledPlugin(plugin.InternalName, plugin.Name, plugin.IsLoaded));
        }

        var found = AutoAnswerPlugins.FirstLoaded(CollectionsMarshal.AsSpan(installed));
        if (found != loaded)
        {
            RunLog.Info(found is null ? "No plugin that answers dialogs on its own is loaded." : $"{found} is loaded and could answer the lottery dialogs; runs and result visits are held until it is turned off.");
        }

        loaded = found;
        return loaded;
    }

    public static bool Blocks => LoadedName() is not null;
}
