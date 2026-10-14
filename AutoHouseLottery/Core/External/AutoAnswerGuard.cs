using System.Runtime.InteropServices;
using AutoHouseLottery.Core.Tasks.Model;
using ECommons.DalamudServices;

namespace AutoHouseLottery.Core.External;

// The shared guard for anything that opens a committing lottery dialog: entry runs, settlement visits and the probe's
// Enter Lottery and View Lottery Results presses. YesAlready does not block, since the controller pauses it while the
// plugin's own work drives the game; any other plugin that answers dialogs does. Dalamud copies its installed plugin list
// on every read, so it is read once a second at most. Framework thread only.
internal static class AutoAnswerGuard
{
    private const long RefreshIntervalMs = 1_000;

    private static readonly List<InstalledPlugin> installed = [];
    private static long refreshedAtMs = long.MinValue;
    private static string? loaded;
    private static string? unpausable;

    // The display name of a loaded plugin that answers dialogs on its own, null when none is loaded.
    public static string? LoadedName()
    {
        Refresh();
        return loaded;
    }

    // The display name of a loaded plugin that answers dialogs and that this plugin cannot pause, null when none is.
    public static string? UnpausableName()
    {
        Refresh();
        return unpausable;
    }

    // Whether a run or a visit may not start at all.
    public static bool Blocks => UnpausableName() is not null;

    // Right before a press that opens a lottery dialog, while the plugin's own work drives the game: whether a loaded
    // plugin could answer that dialog now, because it cannot be paused or its pause is not in effect.
    public static bool Answering => Blocks || (LoadedName() is not null && !YesAlreadyPause.InEffect);

    private static void Refresh()
    {
        var now = Environment.TickCount64;
        if (refreshedAtMs != long.MinValue && now - refreshedAtMs < RefreshIntervalMs)
        {
            return;
        }

        refreshedAtMs = now;
        installed.Clear();
        foreach (var plugin in Svc.PluginInterface.InstalledPlugins)
        {
            installed.Add(new InstalledPlugin(plugin.InternalName, plugin.Name, plugin.IsLoaded));
        }

        var plugins = CollectionsMarshal.AsSpan(installed);
        var foundLoaded = AutoAnswerPlugins.FirstLoaded(plugins);
        var foundUnpausable = foundLoaded is null ? null : AutoAnswerPlugins.FirstUnpausable(plugins, YesAlreadyPause.Reachable);
        if (foundLoaded != loaded || foundUnpausable != unpausable)
        {
            RunLog.Info(foundUnpausable is not null ? $"{foundUnpausable} is loaded and could answer the lottery dialogs; runs and result visits are held until it is turned off."
                : foundLoaded is not null ? $"{foundLoaded} is loaded; it is paused while the plugin works with the lottery dialogs."
                : "No plugin that answers dialogs on its own is loaded.");
        }

        loaded = foundLoaded;
        unpausable = foundUnpausable;
    }
}
