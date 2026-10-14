using AutoHouseLottery.Core.Shared;

namespace AutoHouseLottery.Tests;

public sealed class GilTests
{
    [Theory]
    [InlineData("999", 999)]
    [InlineData("1,234,567", 1_234_567)]
    [InlineData("1.234.567", 1_234_567)]
    [InlineData("  42  ", 42)]
    public void ParseReadsPlainAndGroupedAmounts(string text, long expected)
    {
        Assert.True(Gil.TryParse(text, out var amount));
        Assert.Equal(expected, amount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("1,23")]
    [InlineData("1234,567")]
    [InlineData("1,234,56")]
    [InlineData("99999999999999999999")]
    public void ParseRefusesMalformedAmounts(string text)
    {
        Assert.False(Gil.TryParse(text, out var amount));
        Assert.Equal(0, amount);
    }

    [Fact]
    public void SaturatingAddStopsAtTheLimits()
    {
        Assert.Equal(long.MaxValue, Gil.SaturatingAdd(long.MaxValue, 1));
        Assert.Equal(long.MinValue, Gil.SaturatingAdd(long.MinValue, -1));
        Assert.Equal(7, Gil.SaturatingAdd(3, 4));
    }
}
