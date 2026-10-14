using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Tasks.Model;

internal readonly record struct InstalledPlugin(string InternalName, string Name, bool Loaded);

// Plugins that answer the game's Yes/No and confirmation dialogs on their own. Such a plugin could press Confirm or Yes on
// a lottery dialog the plugin means to check first or to close. YesAlready itself is paused while the plugin works with
// those dialogs; a fork or a renamed build may not honour that pause, so it still has to be turned off.
internal static class AutoAnswerPlugins
{
    public const string PausableInternalName = "YesAlready";

    // Matched case-insensitively anywhere in a plugin's internal name, so forks and renamed builds count too.
    public static readonly ImmutableArray<string> InternalNames = ["YesAlready"];

    // The name of the first loaded auto-answer plugin, null when none is loaded.
    public static string? FirstLoaded(ReadOnlySpan<InstalledPlugin> installed) => First(installed, skipPausable: false);

    // The name of the first loaded auto-answer plugin this plugin cannot pause, null when every loaded one can be paused.
    // YesAlready itself can be paused only while its stop requests can be reached.
    public static string? FirstUnpausable(ReadOnlySpan<InstalledPlugin> installed, bool stopRequestsReachable)
        => First(installed, skipPausable: stopRequestsReachable);

    private static string? First(ReadOnlySpan<InstalledPlugin> installed, bool skipPausable)
    {
        for (var index = 0; index < installed.Length; index++)
        {
            var plugin = installed[index];
            if (plugin.Loaded && IsKnown(plugin.InternalName) && !(skipPausable && IsPausable(plugin.InternalName)))
            {
                return plugin.Name.Length > 0 ? plugin.Name : plugin.InternalName;
            }
        }

        return null;
    }

    private static bool IsPausable(string internalName) => internalName.Equals(PausableInternalName, StringComparison.Ordinal);

    private static bool IsKnown(string internalName)
    {
        for (var index = 0; index < InternalNames.Length; index++)
        {
            if (internalName.Contains(InternalNames[index], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
