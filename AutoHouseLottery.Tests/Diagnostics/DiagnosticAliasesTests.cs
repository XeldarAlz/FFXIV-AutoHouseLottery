using AutoHouseLottery.Core.Diagnostics.Model;

namespace AutoHouseLottery.Tests.Diagnostics;

public sealed class DiagnosticAliasesTests
{
    private const ulong FirstId = 18_014_398_509_481_984;
    private const ulong SecondId = 18_014_398_509_482_001;

    [Fact]
    public void NamesAndContentIdsBecomeTheSameAliasEverywhere()
    {
        var aliases = new DiagnosticAliases([new(FirstId, "Ann Lee"), new(SecondId, "Bo Grant")], null, null);
        var report = aliases.Apply($"Entry run authorized: Ann Lee, content {FirstId}. Settlement scheduler: 1 item due for Bo Grant ({SecondId:X}).");
        Assert.Equal("Entry run authorized: Character A, content Character A ID. Settlement scheduler: 1 item due for Character B (Character B ID).", report);
    }

    [Fact]
    public void ANameInsideALongerNameKeepsEachAlias()
    {
        var aliases = new DiagnosticAliases([new(FirstId, "Ann Lee"), new(SecondId, "Ann Leeds")], null, null);
        Assert.Equal("Character B and Character A", aliases.Apply("Ann Leeds and Ann Lee"));
    }

    [Fact]
    public void TheProbesShortenedIdIsReplacedToo()
    {
        var hex = FirstId.ToString("X");
        var aliases = new DiagnosticAliases([new(FirstId, "Ann Lee")], null, null);
        Assert.Equal("character: content ID Character A ID, name read", aliases.Apply($"character: content ID …{hex[^4..]}, name read"));
    }

    [Fact]
    public void TheWindowsUserFolderInAPathIsHidden()
    {
        var aliases = new DiagnosticAliases([], @"C:\Users\annlee", null);
        Assert.Equal($@"backup at {DiagnosticAliases.UserFolderPlaceholder}\AppData\Roaming\XIVLauncher\pluginConfigs\AutoHouseLottery.json", aliases.Apply(@"backup at C:\Users\annlee\AppData\Roaming\XIVLauncher\pluginConfigs\AutoHouseLottery.json"));
    }

    [Theory]
    [InlineData(@"backup at Z:\home\annlee\.xlcore\pluginConfigs\AutoHouseLottery.json", @"backup at %USERPROFILE%\.xlcore\pluginConfigs\AutoHouseLottery.json")]
    [InlineData("journal at /home/annlee/.xlcore/pluginConfigs/AutoHouseLottery/journal.jsonl", "journal at %USERPROFILE%/.xlcore/pluginConfigs/AutoHouseLottery/journal.jsonl")]
    [InlineData("journal at /Users/annlee/Library/Application Support/XIV on Mac/pluginConfigs", "journal at %USERPROFILE%/Library/Application Support/XIV on Mac/pluginConfigs")]
    [InlineData(@"kept as C:\users\annlee\AppData\Roaming\XIVLauncher\x.json", @"kept as %USERPROFILE%\AppData\Roaming\XIVLauncher\x.json")]
    public void AHomeFolderUnderWineLinuxOrMacOSIsHiddenToo(string line, string expected)
    {
        var aliases = new DiagnosticAliases([], null, null);
        Assert.Equal(expected, aliases.Apply(line));
    }

    [Fact]
    public void ThePluginConfigurationFolderIsHiddenWhereverItLives()
    {
        var aliases = new DiagnosticAliases([], null, @"D:\Games\XIVLauncher\pluginConfigs");
        Assert.Equal($@"backup at {DiagnosticAliases.ConfigFolderPlaceholder}\AutoHouseLottery.json", aliases.Apply(@"backup at D:\Games\XIVLauncher\pluginConfigs\AutoHouseLottery.json"));
    }

    [Fact]
    public void UnknownTextIsLeftAlone()
    {
        var aliases = new DiagnosticAliases([new(0, " "), new(FirstId, "Ann Lee")], null, null);
        Assert.Equal("Placard sale hook: active.", aliases.Apply("Placard sale hook: active."));
    }

    [Theory]
    [InlineData(0, "Character A")]
    [InlineData(25, "Character Z")]
    [InlineData(26, "Character AA")]
    [InlineData(27, "Character AB")]
    public void AliasesCountLikeSpreadsheetColumns(int index, string expected)
    {
        Assert.Equal(expected, DiagnosticAliases.AliasFor(index));
    }
}
