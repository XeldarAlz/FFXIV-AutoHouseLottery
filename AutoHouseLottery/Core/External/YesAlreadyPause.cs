using ECommons.DalamudServices;
using ECommons.EzSharedDataManager;

namespace AutoHouseLottery.Core.External;

// YesAlready answers nothing while its stop requests, a set of plugin names shared through Dalamud, hold any name. This
// plugin adds its own name while its own work drives the lottery dialogs and removes it once that work ends. YesAlready
// empties the set whenever it loads, so a held pause is written again on every frame. Framework thread only.
internal static class YesAlreadyPause
{
    private const string StopRequestsKey = "YesAlready.StopRequests";
    private const long LookupIntervalMs = 1_000;

    private static HashSet<string>? stopRequests;
    private static long nextLookupMs = long.MinValue;
    private static bool held;

    // Whether the stop requests can be reached. Only a YesAlready that reads them creates them.
    public static bool Reachable => StopRequests() is not null;

    // Whether YesAlready is held paused by this plugin right now.
    public static bool InEffect => held && stopRequests is { } requests && requests.Contains(OwnName);

    private static string OwnName => Svc.PluginInterface.Manifest.InternalName;

    public static void Hold(bool hold)
    {
        if (!hold && !held)
        {
            return;
        }

        if (StopRequests() is not { } requests)
        {
            held = false;
            return;
        }

        if (!hold)
        {
            requests.Remove(OwnName);
            held = false;
            RunLog.Info("YesAlready is resumed.");
            return;
        }

        requests.Add(OwnName);
        if (!held)
        {
            held = true;
            RunLog.Info("YesAlready is paused while the plugin works with the lottery dialogs.");
        }
    }

    private static HashSet<string>? StopRequests()
    {
        if (stopRequests is not null)
        {
            return stopRequests;
        }

        var now = Environment.TickCount64;
        if (now < nextLookupMs)
        {
            return null;
        }

        nextLookupMs = now + LookupIntervalMs;
        if (EzSharedData.TryGet<HashSet<string>>(StopRequestsKey, out var found))
        {
            stopRequests = found;
        }

        return stopRequests;
    }
}
