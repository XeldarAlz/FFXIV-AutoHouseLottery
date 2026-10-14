using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Tests;

public sealed class WorldRegionTests
{
    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(3u)]
    [InlineData(4u)]
    public void GlobalClientRegionsAreSupported(uint regionRow)
    {
        Assert.True(WorldRegions.Supported(WorldRegions.FromDataCenterRegion(regionRow)));
    }

    [Theory]
    [InlineData(5u)]
    [InlineData(6u)]
    public void ChineseAndKoreanWorldsAreRefused(uint regionRow)
    {
        Assert.False(WorldRegions.Supported(WorldRegions.FromDataCenterRegion(regionRow)));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(7u)]
    [InlineData(255u)]
    public void AWorldWhoseRegionCannotBeToldIsRefused(uint regionRow)
    {
        Assert.Equal(nameof(WorldRegion.Unknown), WorldRegions.FromDataCenterRegion(regionRow).ToString());
        Assert.False(WorldRegions.Supported(WorldRegions.FromDataCenterRegion(regionRow)));
    }
}
