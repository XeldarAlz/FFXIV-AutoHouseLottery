using AutoHouseLottery.Core.Persistence.Model;

namespace AutoHouseLottery.Tests;

public sealed class SettlementTransitionTests
{
    private const long Deposit = 3_187_500;
    private const long ForfeitRefund = 1_593_750;

    private static readonly DateTime now = new(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void LossRefundPathReachesRefundedAndStops()
    {
        var settlement = Walk(
            EntrySettlement.Entered(Guid.NewGuid(), now),
            SettlementState.ResultsPending,
            SettlementState.InspectingResult,
            SettlementState.LostRefundDue,
            SettlementState.RefundPending,
            SettlementState.Refunded);

        Assert.True(settlement.IsSettled);
        Assert.Equal(RefundKind.Loss, settlement.Refund);
        Assert.Equal(Deposit, settlement.ReceivedRefund);
        Assert.False(settlement.TryAdvance(SettlementState.RefundPending, null, now, out _));
    }

    [Fact]
    public void WinNeverTakesTheLossRefundPath()
    {
        var won = Walk(EntrySettlement.Entered(Guid.NewGuid(), now), SettlementState.ResultsPending, SettlementState.InspectingResult, SettlementState.WonUnclaimed);

        Assert.False(won.TryAdvance(SettlementState.LostRefundDue, Deposit, now, out _));
        Assert.False(won.TryAdvance(SettlementState.RefundPending, Deposit, now, out _));
        Assert.False(won.TryAdvance(SettlementState.Refunded, Deposit, now, out _));
    }

    [Fact]
    public void ForfeitRefundKeepsItsOwnKindAndAmount()
    {
        var pending = Walk(
            EntrySettlement.Entered(Guid.NewGuid(), now),
            SettlementState.ResultsPending,
            SettlementState.InspectingResult,
            SettlementState.WonUnclaimed,
            SettlementState.ForfeitRefundDue,
            SettlementState.RefundPending);

        Assert.Equal(RefundKind.Forfeit, pending.Refund);
        Assert.False(pending.TryAdvance(SettlementState.LostRefundDue, Deposit, now, out _));
        Assert.True(pending.TryAdvance(SettlementState.ForfeitRefundDue, ForfeitRefund, now, out var backToDue));
        Assert.Equal(ForfeitRefund, backToDue.ExpectedRefund);
    }

    [Fact]
    public void OnlyPendingActionsCanNeedReconciliation()
    {
        var entered = EntrySettlement.Entered(Guid.NewGuid(), now);
        var claimPending = Walk(entered, SettlementState.ResultsPending, SettlementState.InspectingResult, SettlementState.WonUnclaimed, SettlementState.ClaimPending);

        Assert.False(entered.TryAdvance(SettlementState.NeedsReconciliation, null, now, out _));
        Assert.True(claimPending.TryAdvance(SettlementState.NeedsReconciliation, null, now, out var uncertain));
        Assert.False(uncertain.TryAdvance(SettlementState.ClaimPending, null, now, out _));
    }

    [Fact]
    public void SettledStatesHaveNoWayOut()
    {
        var states = Enum.GetValues<SettlementState>();
        for (var fromIndex = 0; fromIndex < states.Length; fromIndex++)
        {
            if (!SettlementTransitions.IsSettled(states[fromIndex]))
            {
                continue;
            }

            for (var toIndex = 0; toIndex < states.Length; toIndex++)
            {
                Assert.False(SettlementTransitions.IsLegal(states[fromIndex], states[toIndex]));
            }
        }
    }

    [Fact]
    public void AttemptCannotBeConfirmedWithoutDispatchOrReconciliation()
    {
        Assert.False(AttemptTransitions.IsLegal(AttemptState.Prepared, AttemptState.Confirmed));
        Assert.True(AttemptTransitions.IsLegal(AttemptState.NeedsReconciliation, AttemptState.Confirmed));
        Assert.False(AttemptTransitions.IsLegal(AttemptState.Confirmed, AttemptState.Rejected));
        Assert.False(AttemptTransitions.IsLegal(AttemptState.Rejected, AttemptState.Prepared));
    }

    private static EntrySettlement Walk(EntrySettlement start, params SettlementState[] steps)
    {
        var settlement = start;
        for (var index = 0; index < steps.Length; index++)
        {
            var amount = steps[index] == SettlementState.ForfeitRefundDue ? ForfeitRefund : Deposit;
            Assert.True(settlement.TryAdvance(steps[index], amount, now, out var advanced));
            settlement = advanced;
        }

        return settlement;
    }
}
