using AutoHouseLottery.Core.Changelog;
using System.IO;
using System.Text.RegularExpressions;

namespace AutoHouseLottery.Tests;

public sealed partial class ChangelogTests
{
    [Fact]
    public void LatestEntryMatchesThePluginVersion()
    {
        var properties = File.ReadAllText(Path.Combine(RepositoryFiles.Root, "Directory.Build.props"));
        var match = VersionPattern().Match(properties);
        Assert.True(match.Success, "Directory.Build.props carries no <Version>.");
        Assert.Equal(match.Groups[1].Value, ChangelogData.LatestVersion);
    }

    [Fact]
    public void EveryEntryHasHighlights()
    {
        var entries = ChangelogData.Entries;
        for (var index = 0; index < entries.Length; index++)
        {
            Assert.NotEmpty(entries[index].Highlights);
        }
    }

    [Fact]
    public void UnseenCountStartsFromTheLastSeenVersion()
    {
        Assert.Equal(0, ChangelogData.UnseenCount(ChangelogData.LatestVersion));
        Assert.Equal(ChangelogData.Entries.Length, ChangelogData.UnseenCount(string.Empty));
    }

    [GeneratedRegex(@"<Version>([\d.]+)</Version>")]
    private static partial Regex VersionPattern();
}
