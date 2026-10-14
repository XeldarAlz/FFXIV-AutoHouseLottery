using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Commands.Model;

// Every subcommand of the plugin's chat commands, in the order the command help lists them. The plugin dispatches from
// this table, and the tests check the README and the command help in every catalog against it.
internal static class PluginCommands
{
    public const string Primary = "/autohouselottery";
    public const string Alias = "/ahl";

    public static readonly ImmutableArray<CommandVerb> All =
    [
        new(PluginCommand.Settings, "config", CommandAudience.Player),
        new(PluginCommand.MyLottery, "lottery", CommandAudience.Player),
        new(PluginCommand.Saved, "saved", CommandAudience.Player),
        new(PluginCommand.Plugins, "deps", CommandAudience.Player, "dependencies"),
        new(PluginCommand.Console, "log", CommandAudience.Player),
        new(PluginCommand.Changelog, "changelog", CommandAudience.Player),
        new(PluginCommand.About, "about", CommandAudience.Player),
        new(PluginCommand.Stop, "stop", CommandAudience.Player),
        new(PluginCommand.Pause, "pause", CommandAudience.Player),
        new(PluginCommand.Resume, "resume", CommandAudience.Player),
        new(PluginCommand.Target, "target", CommandAudience.Player),
        new(PluginCommand.Probe, "probe", CommandAudience.Testing),
        new(PluginCommand.Dev, "dev", CommandAudience.Testing),
    ];

    // A player command takes no arguments, so anything after it toggles the window as an unknown command does. The
    // testing commands hand the rest of the line to their own parser.
    public static PluginCommand Parse(string arguments, out string rest)
    {
        rest = string.Empty;
        var trimmed = arguments.AsSpan().Trim();
        var wordEnd = WordEnd(trimmed);
        var word = wordEnd < 0 ? trimmed : trimmed[..wordEnd];
        var remainder = wordEnd < 0 ? ReadOnlySpan<char>.Empty : trimmed[wordEnd..].Trim();
        for (var index = 0; index < All.Length; index++)
        {
            var verb = All[index];
            if (!verb.Answers(word))
            {
                continue;
            }

            if (verb.TakesArguments)
            {
                rest = remainder.ToString();
                return verb.Command;
            }

            return remainder.IsEmpty ? verb.Command : PluginCommand.ToggleWindow;
        }

        return PluginCommand.ToggleWindow;
    }

    // Any whitespace ends the word, including the full-width space a Japanese input method types.
    private static int WordEnd(ReadOnlySpan<char> text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                return index;
            }
        }

        return -1;
    }
}
