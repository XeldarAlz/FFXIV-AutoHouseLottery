using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Tasks.Model;

internal readonly record struct InstalledPlugin(string InternalName, string Name, bool Loaded);

// Plugins that answer the game's Yes/No and confirmation dialogs on their own. While one is loaded, nothing that opens a
// committing lottery dialog may start, dry run or not: a dry run opens the real entry confirmation and the real result
// dialogs, and such a plugin could press Confirm or Yes on them before the plugin closes them.
internal static class AutoAnswerPlugins
{
    // Matched case-insensitively anywhere in a plugin's internal name, so forks and renamed builds count too.
    public static readonly ImmutableArray<string> InternalNames = ["YesAlready"];

    // The name of the first loaded auto-answer plugin, null when none is loaded.
    public static string? FirstLoaded(ReadOnlySpan<InstalledPlugin> installed)
    {
        for (var index = 0; index < installed.Length; index++)
        {
            var plugin = installed[index];
            if (plugin.Loaded && IsKnown(plugin.InternalName))
            {
                return plugin.Name.Length > 0 ? plugin.Name : plugin.InternalName;
            }
        }

        return null;
    }

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
