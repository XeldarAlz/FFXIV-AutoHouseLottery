using AutoHouseLottery.Core.Game.Model;

namespace AutoHouseLottery.Tests.Game;

public sealed class WardLayoutTests
{
    private const int FullWardValues = 36 + (60 * 7);

    [Theory]
    [InlineData(60, FullWardValues, true)]
    [InlineData(60, FullWardValues - 1, false)]
    [InlineData(61, FullWardValues + 7, false)]
    [InlineData(-1, FullWardValues, false)]
    [InlineData(0, 36, true)]
    [InlineData(30, 36 + (30 * 7), true)]
    public void RowsFitOnlyWhenEveryRowHasItsSevenValues(int rowCount, int valueCount, bool expected)
    {
        Assert.Equal(expected, WardLayout.RowCountFits(rowCount, valueCount));
    }

    [Fact]
    public void TheLastRowsLastValueIsInsideAFullWard()
    {
        Assert.Equal(FullWardValues - 1, WardLayout.RowValue(59, WardLayout.FirstTagOffset + WardLayout.TagCount - 1));
    }

    [Theory]
    [InlineData(0x01000000u, true, false)]
    [InlineData(0x02000000u, false, true)]
    [InlineData(0x03000000u, true, true)]
    [InlineData(0x00000003u, false, false)]
    public void BuyerFlagsAreTheTopBytesLowBits(uint buyerFlags, bool freeCompany, bool privateBuyer)
    {
        Assert.Equal(freeCompany, WardLayout.FreeCompanyFlag(buyerFlags));
        Assert.Equal(privateBuyer, WardLayout.PrivateFlag(buyerFlags));
    }
}
