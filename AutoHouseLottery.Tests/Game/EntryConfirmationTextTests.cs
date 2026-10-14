using AutoHouseLottery.Core.Game.Model;

namespace AutoHouseLottery.Tests.Game;

public sealed class EntryConfirmationTextTests
{
    [Theory]
    [InlineData("3,187,500", "personal")]
    [InlineData("3.187.500", "Einzelperson")]
    [InlineData("3 187 500", "")]
    [InlineData("3 187 500", "")]
    public void DepositIsTheFirstAmountWhateverTheGrouping(string amount, string buyer)
    {
        Assert.Equal(3_187_500, EntryConfirmationText.Deposit([buyer, amount, "free companies"]));
        Assert.Equal(3_187_500, EntryConfirmationText.Deposit([amount, buyer]));
    }

    [Fact]
    public void NoAmountReadsAsUnknownNeverZero()
    {
        Assert.Null(EntryConfirmationText.Deposit([]));
        Assert.Null(EntryConfirmationText.Deposit(["personal", "free companies"]));
        Assert.Null(EntryConfirmationText.Deposit(["0"]));
    }

    [Fact]
    public void AmountBeyondTheGilRangeIsUnknown()
    {
        Assert.Null(EntryConfirmationText.Deposit(["99999999999999999999"]));
    }
}
