using AutoHouseLottery.Core.Game.Model;

namespace AutoHouseLottery.Tests.Game;

public sealed class EntryConfirmationTextTests
{
    private const long Amount = 3_187_500;

    [Theory]
    [InlineData("3,187,500", "personal")]
    [InlineData("3.187.500", "Einzelperson")]
    [InlineData("3 187 500", "")]
    [InlineData("3 187 500", "")]
    [InlineData("3 187 500", "")]
    [InlineData("3 187 500", "")]
    [InlineData("3'187'500", "")]
    [InlineData("3，187，500", "")]
    [InlineData("3187500", "")]
    public void DepositReadsThroughEachClientsGrouping(string amount, string buyer)
    {
        Assert.Equal(Amount, EntryConfirmationText.Deposit([buyer, amount, "free companies"]));
        Assert.Equal(Amount, EntryConfirmationText.Deposit([amount, buyer]));
    }

    [Theory]
    [InlineData("Plot 12 3,187,500")]
    [InlineData("12 3,187,500")]
    [InlineData("3,18")]
    [InlineData("3.5")]
    [InlineData("12345,678")]
    [InlineData("3,1875,00")]
    [InlineData("318万7500")]
    [InlineData("318万")]
    [InlineData("３１８７５００")]
    [InlineData("3,187,500 and 12")]
    public void AnythingButOneWellFormedAmountIsUnknown(string shown)
    {
        Assert.Null(EntryConfirmationText.Deposit([shown]));
    }

    [Fact]
    public void DigitsInTwoMacrosAreUnknown()
    {
        Assert.Null(EntryConfirmationText.Deposit(["3,187,500", "12"]));
    }

    [Theory]
    [InlineData("99999999999999999999")]
    [InlineData("9,223,372,036,854,775,808")]
    public void AmountBeyondTheGilRangeIsUnknownNotWrapped(string shown)
    {
        Assert.Null(EntryConfirmationText.Deposit([shown]));
    }

    [Fact]
    public void NoAmountReadsAsUnknownNeverZero()
    {
        Assert.Null(EntryConfirmationText.Deposit([]));
        Assert.Null(EntryConfirmationText.Deposit(["personal", "free companies"]));
        Assert.Null(EntryConfirmationText.Deposit(["0"]));
    }
}
