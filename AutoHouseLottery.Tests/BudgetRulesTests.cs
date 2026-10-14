using AutoHouseLottery.Core.Eligibility.Model;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests;

public sealed class BudgetRulesTests
{
    private const long Reserve = 100_000;
    private const long Travel = 10_000;
    private const long ExactlyEnoughGil = Deposit + Travel + Reserve;

    private static readonly CharacterBudget budget = new(Deposit, Reserve, Travel);

    [Fact]
    public void DepositAtTheExactCapWithTheExactReserveFits()
    {
        Assert.Equal(BudgetCheck.Fits, BudgetRules.Check(budget, default, Deposit, ExactlyEnoughGil));
    }

    [Fact]
    public void OneGilOverTheCapIsRefused()
    {
        Assert.Equal(BudgetCheck.OverDepositCap, BudgetRules.Check(budget, default, Deposit + 1, long.MaxValue / 2));
    }

    [Fact]
    public void OneGilShortOfTheReserveIsRefused()
    {
        Assert.Equal(BudgetCheck.BelowReserve, BudgetRules.Check(budget, default, Deposit, ExactlyEnoughGil - 1));
    }

    [Fact]
    public void MissingBalanceIsUnknownNotAffordable()
    {
        Assert.Equal(BudgetCheck.BalanceUnknown, BudgetRules.Check(budget, default, Deposit, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-5L)]
    public void MissingOrNonPositiveDepositIsNeverUnlimited(long? deposit)
    {
        Assert.Equal(BudgetCheck.DepositUnknown, BudgetRules.Check(budget, default, deposit, ExactlyEnoughGil));
    }

    [Fact]
    public void LargeTotalsOverflowInsteadOfWrapping()
    {
        var hugeTravel = budget with { DepositCap = long.MaxValue, TravelAllowance = long.MaxValue };
        var hugePending = new DepositLedger(0, long.MaxValue, 0);

        Assert.Equal(BudgetCheck.Overflow, BudgetRules.Check(hugeTravel, default, Deposit, long.MaxValue));
        Assert.Equal(BudgetCheck.Overflow, BudgetRules.Check(budget, hugePending, 1, long.MaxValue));
    }

    [Fact]
    public void PendingReservationsAfterADisconnectCountAgainstTheCap()
    {
        Assert.True(default(DepositLedger).TryReserve(Deposit, out var pending));

        Assert.Equal(BudgetCheck.OverDepositCap, BudgetRules.Check(budget, pending, 1, long.MaxValue / 2));
    }

    [Fact]
    public void ReceivedRefundNeverExpandsTheGrossDepositCap()
    {
        Assert.True(default(DepositLedger).TryReserve(Deposit, out var reserved));
        Assert.True(reserved.TryConfirm(Deposit, out var submitted));
        Assert.True(submitted.TryRecordRefund(Deposit, out var refunded));

        Assert.Equal(Deposit, refunded.GrossSubmitted);
        Assert.Equal(BudgetCheck.OverDepositCap, BudgetRules.Check(budget, refunded, Deposit, long.MaxValue / 2));
    }

    [Fact]
    public void LedgerRefusesImpossibleMovements()
    {
        var ledger = new DepositLedger(0, 1, 0);

        Assert.False(ledger.TryReserve(long.MaxValue, out _));
        Assert.False(ledger.TryRelease(2, out _));
        Assert.False(ledger.TryConfirm(2, out _));
        Assert.False(ledger.TryReserve(0, out _));
        Assert.True(ledger.TryRelease(1, out var released));
        Assert.Equal(0, released.PendingReservations);
    }
}
