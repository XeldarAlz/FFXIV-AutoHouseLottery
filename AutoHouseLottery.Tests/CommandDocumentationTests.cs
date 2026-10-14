using AutoHouseLottery.Core.Commands.Model;
using AutoHouseLottery.Core.Localization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AutoHouseLottery.Tests;

// The README's two command sections and the command help in every catalog are checked against the plugin's command
// table, so a command that is added, renamed or removed cannot leave the documentation behind.
public sealed partial class CommandDocumentationTests
{
    private const string ReadmePath = "README.md";
    private const string CommandsHeading = "## Commands";
    private const string TestingHeading = "## Testing and debug commands";
    private const string SectionPrefix = "## ";
    private const string HelpKey = "plugin.commandHelp";
    private const char HelpSeparator = '|';
    private const string TableCellSeparator = @" \| ";

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

    [Fact]
    public void ReadmeCommandsTableListsEveryPlayerCommand()
    {
        var section = Section(CommandsHeading);
        Assert.Contains($"`{PluginCommands.Primary}`", section, StringComparison.Ordinal);
        Assert.Contains($"`{PluginCommands.Alias}`", section, StringComparison.Ordinal);
        for (var index = 0; index < PluginCommands.All.Length; index++)
        {
            var verb = PluginCommands.All[index];
            if (verb.Audience != CommandAudience.Player)
            {
                continue;
            }

            Assert.True(section.Contains($"`{PluginCommands.Primary} {verb.Verb}`", StringComparison.Ordinal), $"The README's Commands table does not list {verb.Verb}.");
            if (verb.Alias is { } alias)
            {
                Assert.True(section.Contains($"`{PluginCommands.Primary} {alias}`", StringComparison.Ordinal), $"The README's Commands table does not mention the alias {alias}.");
            }
        }
    }

    [Fact]
    public void ReadmeTestingSectionListsEveryTestingCommandAndItsWords()
    {
        var section = Section(TestingHeading);
        for (var index = 0; index < PluginCommands.All.Length; index++)
        {
            var verb = PluginCommands.All[index];
            if (verb.Audience == CommandAudience.Testing)
            {
                Assert.True(section.Contains($"`{PluginCommands.Primary} {verb.Verb}", StringComparison.Ordinal), $"The README's testing section does not list {verb.Verb}.");
            }
        }

        AssertListed(section, "probe", ProbeVerbs.All);
        AssertListed(section, "dev", DevVerbs.All);
        var pressTargets = $"{ProbeVerbs.Press} <{string.Join(TableCellSeparator, ProbeVerbs.PressTargets)}>";
        Assert.True(section.Contains(pressTargets, StringComparison.Ordinal), $"The README's testing section does not show {pressTargets}.");
    }

    [Fact]
    public void ReadmeNamesOnlyCommandsThePluginHasInTheRightSection()
    {
        AssertKnown(Section(CommandsHeading), CommandAudience.Player);
        AssertKnown(Section(TestingHeading), CommandAudience.Testing);
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

    private static void AssertListed(string section, string command, System.Collections.Immutable.ImmutableArray<string> words)
    {
        for (var index = 0; index < words.Length; index++)
        {
            Assert.True(section.Contains($"`{PluginCommands.Primary} {command} {words[index]}", StringComparison.Ordinal), $"The README's testing section does not list {command} {words[index]}.");
        }
    }

    private static void AssertKnown(string section, CommandAudience audience)
    {
        var mentions = MentionPattern().Matches(section);
        for (var index = 0; index < mentions.Count; index++)
        {
            var word = mentions[index].Groups[1].Value;
            var found = false;
            for (var verbIndex = 0; verbIndex < PluginCommands.All.Length; verbIndex++)
            {
                var verb = PluginCommands.All[verbIndex];
                found |= verb.Audience == audience && verb.Answers(word);
            }

            Assert.True(found, $"The README's {audience} section names {PluginCommands.Primary} {word}, which is not a {audience} command of the plugin.");
            var subword = mentions[index].Groups[2].Value;
            if (subword.Length == 0)
            {
                continue;
            }

            var words = word == "probe" ? ProbeVerbs.All : word == "dev" ? DevVerbs.All : [];
            Assert.True(words.Contains(subword), $"The README names {PluginCommands.Primary} {word} {subword}, which the plugin does not have.");
        }
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

    private static string Section(string heading)
    {
        var readme = File.ReadAllText(Path.Combine(RepositoryFiles.Root, ReadmePath));
        var start = readme.IndexOf(string.Concat("\n", heading, "\n"), StringComparison.Ordinal);
        Assert.True(start >= 0, $"The README has no '{heading}' section.");
        var end = readme.IndexOf(string.Concat("\n", SectionPrefix), start + heading.Length + 1, StringComparison.Ordinal);
        return end < 0 ? readme[start..] : readme[start..end];
    }

    [GeneratedRegex(@"/autohouselottery ([a-z][a-z-]*)(?: ([a-z][a-z-]*))?")]
    private static partial Regex MentionPattern();

    [GeneratedRegex(@"^[a-z][a-z-]*")]
    private static partial Regex LeadingWord();
}
