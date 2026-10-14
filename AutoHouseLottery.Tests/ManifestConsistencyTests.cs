using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AutoHouseLottery.Tests;

// The plugin's manifest ships inside the release zip and repo.json is what Dalamud lists, so the two must describe the
// same plugin, and Dalamud refuses an install whose version differs from repo.json.
public sealed partial class ManifestConsistencyTests
{
    private const string ManifestPath = "AutoHouseLottery/AutoHouseLottery.json";
    private const string RepositoryListingPath = "repo.json";
    private const string BuildPropertiesPath = "Directory.Build.props";
    private const int VersionParts = 4;

    private static readonly string[] sharedTextFields = ["Name", "InternalName", "Punchline", "Description", "RepoUrl", "IconUrl"];
    private static readonly string[] listedVersionFields = ["AssemblyVersion", "TestingAssemblyVersion"];

    public static TheoryData<string> SharedTextFields()
    {
        var data = new TheoryData<string>();
        for (var index = 0; index < sharedTextFields.Length; index++)
        {
            data.Add(sharedTextFields[index]);
        }

        return data;
    }

    public static TheoryData<string> ListedVersionFields()
    {
        var data = new TheoryData<string>();
        for (var index = 0; index < listedVersionFields.Length; index++)
        {
            data.Add(listedVersionFields[index]);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SharedTextFields))]
    public void ManifestAndListingAgreeOn(string field)
    {
        using var manifest = ReadManifest();
        using var listing = ReadListingEntry();
        var shipped = manifest.RootElement.GetProperty(field).GetString();
        var listed = listing.RootElement[0].GetProperty(field).GetString();
        Assert.False(string.IsNullOrWhiteSpace(shipped), $"{ManifestPath} has no {field}.");
        Assert.Equal(shipped, listed);
    }

    [Fact]
    public void ManifestAndListingCarryTheSameTagsInTheSameOrder()
    {
        using var manifest = ReadManifest();
        using var listing = ReadListingEntry();
        var shipped = Strings(manifest.RootElement.GetProperty("Tags"));
        var listed = Strings(listing.RootElement[0].GetProperty("Tags"));
        Assert.NotEmpty(shipped);
        Assert.Equal(shipped, listed);
    }

    [Theory]
    [MemberData(nameof(ListedVersionFields))]
    public void ListingVersionMatchesTheBuildVersion(string field)
    {
        using var listing = ReadListingEntry();
        Assert.Equal(BuildVersion(), listing.RootElement[0].GetProperty(field).GetString());
    }

    [Fact]
    public void BuildVersionHasFourParts()
    {
        Assert.Equal(VersionParts, BuildVersion().Split('.').Length);
    }

    private static string BuildVersion()
    {
        var properties = File.ReadAllText(Path.Combine(RepositoryFiles.Root, BuildPropertiesPath));
        var match = VersionPattern().Match(properties);
        Assert.True(match.Success, $"{BuildPropertiesPath} carries no <Version>.");
        return match.Groups[1].Value;
    }

    private static JsonDocument ReadManifest() => JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryFiles.Root, ManifestPath)));

    private static JsonDocument ReadListingEntry()
    {
        var listing = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryFiles.Root, RepositoryListingPath)));
        Assert.Equal(JsonValueKind.Array, listing.RootElement.ValueKind);
        Assert.Equal(1, listing.RootElement.GetArrayLength());
        return listing;
    }

    private static List<string> Strings(JsonElement array)
    {
        var values = new List<string>(array.GetArrayLength());
        for (var index = 0; index < array.GetArrayLength(); index++)
        {
            values.Add(array[index].GetString() ?? string.Empty);
        }

        return values;
    }

    [GeneratedRegex(@"<Version>([\d.]+)</Version>")]
    private static partial Regex VersionPattern();
}
