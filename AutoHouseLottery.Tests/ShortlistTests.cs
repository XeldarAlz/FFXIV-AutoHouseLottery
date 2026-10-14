using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Selection.Model;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests;

public sealed class ShortlistTests
{
    private static readonly Guid shortlistId = new("6f1c3a52-4a8e-4f53-9d55-0a8f0e6c2b11");

    [Fact]
    public void AddingAllMatchingTwiceAddsNoDuplicates()
    {
        var shortlist = new Shortlist(shortlistId, "Small plots");
        var matching = Wards(1, 42);

        Assert.Equal(42, shortlist.AddRange(matching));
        Assert.Equal(0, shortlist.AddRange(matching));
        Assert.Equal(42, shortlist.Count);
        Assert.Equal(1, shortlist.Revision);
    }

    [Fact]
    public void DeduplicatesByFullPlotIdentityOnly()
    {
        var shortlist = new Shortlist(shortlistId, "Two worlds");

        Assert.True(shortlist.Add(Plot(12, 5)));
        Assert.True(shortlist.Add(Plot(12, 5, world: OtherWorld)));
        Assert.False(shortlist.Add(Plot(12, 5)));
        Assert.Equal(2, shortlist.Count);
    }

    [Fact]
    public void ChangingFiltersDoesNotRemoveShortlistedPlots()
    {
        var shortlist = new Shortlist(shortlistId, "Filters");
        shortlist.AddRange([Plot(1, 1), Plot(1, 2)]);

        shortlist.AddRange([Plot(1, 2), Plot(2, 1)]);

        Assert.True(shortlist.Contains(Plot(1, 1)));
        Assert.Equal([Plot(1, 1), Plot(1, 2), Plot(2, 1)], shortlist.Plots);
    }

    [Fact]
    public void FrozenSnapshotIgnoresLaterEdits()
    {
        var shortlist = new Shortlist(shortlistId, "Frozen");
        shortlist.AddRange([Plot(1, 1), Plot(1, 2)]);

        var frozen = shortlist.Freeze();
        shortlist.Remove(Plot(1, 1));
        shortlist.Add(Plot(3, 3));

        Assert.Equal<PlotKey>([Plot(1, 1), Plot(1, 2)], frozen.Plots);
        Assert.Equal(1, frozen.Revision);
        Assert.Equal(3, shortlist.Revision);
    }

    [Fact]
    public void RevisionMovesOnlyWhenContentChanges()
    {
        var shortlist = new Shortlist(shortlistId, "Revisions");

        Assert.False(shortlist.Remove(Plot(1, 1)));
        shortlist.Clear();
        Assert.Equal(0, shortlist.Revision);

        shortlist.Add(Plot(1, 1));
        shortlist.Clear();
        Assert.Equal(2, shortlist.Revision);
        Assert.Equal(0, shortlist.Count);
    }

    [Fact]
    public void ExportAndImportKeepEveryFullPlotKey()
    {
        var shortlist = new Shortlist(shortlistId, "Portable");
        shortlist.AddRange([Plot(30, 60, district: HousingDistricts.Empyreum), Plot(1, 1, region: GameRegion.China), Plot(12, 31, world: OtherWorld)]);

        Assert.Equal(ShortlistImportError.None, ShortlistTransfer.TryImport(ShortlistTransfer.Export(shortlist), out var imported));

        Assert.Equal(shortlistId, imported!.Id);
        Assert.Equal("Portable", imported.Name);
        Assert.Equal(shortlist.Plots, imported.Plots);
    }

    [Fact]
    public void ImportRejectsOutOfRangePlotsInsteadOfClamping()
    {
        const string json = """{"schema":1,"id":"6f1c3a52-4a8e-4f53-9d55-0a8f0e6c2b11","name":"Bad","plots":[{"region":"Global","world":63,"district":339,"ward":31,"plot":1}]}""";

        Assert.Equal(ShortlistImportError.InvalidPlot, ShortlistTransfer.TryImport(json, out var imported));
        Assert.Null(imported);
    }

    [Fact]
    public void ImportRejectsMalformedAndUnsupportedDocuments()
    {
        Assert.Equal(ShortlistImportError.Malformed, ShortlistTransfer.TryImport("{ not json", out _));
        Assert.Equal(ShortlistImportError.UnsupportedSchema, ShortlistTransfer.TryImport("""{"schema":2,"plots":[]}""", out _));
        Assert.Equal(ShortlistImportError.MissingField, ShortlistTransfer.TryImport("""{"schema":1,"name":"No id","plots":[]}""", out _));
        Assert.Equal(ShortlistImportError.Malformed, ShortlistTransfer.TryImport("""{"schema":1,"id":"6f1c3a52-4a8e-4f53-9d55-0a8f0e6c2b11","name":"x","plots":[{"region":"Atlantis"}]}""", out _));
    }

    private static PlotKey[] Wards(int ward, int plotCount)
    {
        var keys = new PlotKey[plotCount];
        for (var index = 0; index < plotCount; index++)
        {
            keys[index] = Plot(ward, index + 1);
        }

        return keys;
    }
}
