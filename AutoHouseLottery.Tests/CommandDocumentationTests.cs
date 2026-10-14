using AutoHouseLottery.Core.Commands.Model;
using AutoHouseLottery.Core.Localization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AutoHouseLottery.Tests;

// The command help in every catalog is checked against the plugin's command table, so a command that is added, renamed
// or removed cannot leave the help behind.
public sealed partial class CommandDocumentationTests
{
    private const string HelpKey = "plugin.commandHelp";
    private const char HelpSeparator = '|';

    private static readonly string[] languageCodes = ["en", "de", "es", "fr", "ja", "pt", "ru", "tr", "zh"];

    public static TheoryData<string> Catalogs()
    {
        var data = new TheoryData<string>();
        for (var index = 0; index < languageCodes.Length; index++)
        {
            data.Add(languageCodes[index]);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Catalogs))]
    public void CommandHelpListsExactlyThePlayerCommands(string languageCode)
    {
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryFiles.CatalogDirectory, string.Concat(languageCode, ".json"))));
        var help = catalog.RootElement.GetProperty(HelpKey).GetString() ?? string.Empty;
        Assert.Equal(PlayerVerbs(), HelpVerbs(help));
    }

    [Fact]
    public void EnglishCommandHelpListsExactlyThePlayerCommands()
    {
        Assert.Equal(PlayerVerbs(), HelpVerbs(L.Plugin.CommandHelp.Source));
    }

    [Theory]
    [InlineData("config", nameof(PluginCommand.Settings))]
    [InlineData("  CONFIG  ", nameof(PluginCommand.Settings))]
    [InlineData("dependencies", nameof(PluginCommand.Plugins))]
    [InlineData("resume", nameof(PluginCommand.Resume))]
    [InlineData("", nameof(PluginCommand.ToggleWindow))]
    [InlineData("config extra", nameof(PluginCommand.ToggleWindow))]
    [InlineData("probing", nameof(PluginCommand.ToggleWindow))]
    [InlineData("unknown", nameof(PluginCommand.ToggleWindow))]
    public void ParseFindsTheCommand(string arguments, string expected)
    {
        Assert.Equal(Enum.Parse<PluginCommand>(expected), PluginCommands.Parse(arguments, out _));
    }

    [Fact]
    public void ParseHandsTheRestToATestingCommand()
    {
        Assert.Equal(PluginCommand.Dev, PluginCommands.Parse(" dev  run mist 3 7 3187500 ", out var rest));
        Assert.Equal("run mist 3 7 3187500", rest);
        Assert.Equal(PluginCommand.Probe, PluginCommands.Parse("probe", out var empty));
        Assert.Equal(string.Empty, empty);
    }

    private static SortedSet<string> PlayerVerbs()
    {
        var verbs = new SortedSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < PluginCommands.All.Length; index++)
        {
            if (PluginCommands.All[index].Audience == CommandAudience.Player)
            {
                verbs.Add(PluginCommands.All[index].Verb);
            }
        }

        return verbs;
    }

    private static SortedSet<string> HelpVerbs(string help)
    {
        var start = help.IndexOf(PluginCommands.Primary, StringComparison.Ordinal);
        Assert.True(start >= 0, $"The command help does not name {PluginCommands.Primary}.");
        var segments = help[(start + PluginCommands.Primary.Length)..].Split(HelpSeparator);
        var verbs = new SortedSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < segments.Length; index++)
        {
            var verb = LeadingWord().Match(segments[index].Trim());
            Assert.True(verb.Success, $"A part of the command help names no command: '{segments[index]}'.");
            verbs.Add(verb.Value);
        }

        return verbs;
    }

    [GeneratedRegex(@"^[a-z][a-z-]*")]
    private static partial Regex LeadingWord();
}
