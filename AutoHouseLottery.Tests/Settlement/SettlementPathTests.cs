using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Settlement.Model;

namespace AutoHouseLottery.Tests.Settlement;

public sealed class SettlementPathTests
{
    [Fact]
    public void AFirstResultWalksThroughTheStatesTheJournalRequires()
    {
        Assert.Equal([SettlementState.ResultsPending, SettlementState.InspectingResult, SettlementState.LostRefundDue], Plan(SettlementState.Entered, SettlementState.LostRefundDue));
        Assert.Equal([SettlementState.InspectingResult, SettlementState.WonUnclaimed], Plan(SettlementState.OutcomeUnknown, SettlementState.WonUnclaimed));
        Assert.Equal([SettlementState.InspectingResult, SettlementState.LostRefundDue], Plan(SettlementState.NeedsReconciliation, SettlementState.LostRefundDue));
        Assert.Equal([SettlementState.ForfeitRefundDue], Plan(SettlementState.WonUnclaimed, SettlementState.ForfeitRefundDue));
        Assert.Equal([SettlementState.ForfeitRefundDue, SettlementState.Refunded], Plan(SettlementState.WonUnclaimed, SettlementState.Refunded));
        Assert.Equal([SettlementState.InspectingResult, SettlementState.Refunded], Plan(SettlementState.ResultsPending, SettlementState.Refunded));
    }

    [Fact]
    public void NoWalkInventsAPressOrAQuestionAboutOne()
    {
        Span<SettlementState> steps = stackalloc SettlementState[8];
        Assert.Equal(SettlementPath.Unreachable, SettlementPath.Plan(SettlementState.Entered, SettlementState.RefundPending, steps));
        Assert.Equal(SettlementPath.Unreachable, SettlementPath.Plan(SettlementState.Entered, SettlementState.ClaimPending, steps));
        Assert.Equal(SettlementPath.Unreachable, SettlementPath.Plan(SettlementState.Entered, SettlementState.NeedsReconciliation, steps));
        Assert.Equal(SettlementPath.Unreachable, SettlementPath.Plan(SettlementState.ClaimPending, SettlementState.Refunded, steps));
    }

    [Fact]
    public void AlreadyThereIsNoStep()
    {
        Span<SettlementState> steps = stackalloc SettlementState[4];
        Assert.Equal(0, SettlementPath.Plan(SettlementState.LostRefundDue, SettlementState.LostRefundDue, steps));
    }

    [Fact]
    public void NoWalkTurnsALossIntoAWinOrReopensASettledEntry()
    {
        Span<SettlementState> steps = stackalloc SettlementState[8];
        Assert.Equal(SettlementPath.Unreachable, SettlementPath.Plan(SettlementState.LostRefundDue, SettlementState.WonUnclaimed, steps));
        Assert.Equal(SettlementPath.Unreachable, SettlementPath.Plan(SettlementState.LostRefundDue, SettlementState.Claimed, steps));
        Assert.Equal(SettlementPath.Unreachable, SettlementPath.Plan(SettlementState.Refunded, SettlementState.LostRefundDue, steps));
        Assert.Equal(SettlementPath.Unreachable, SettlementPath.Plan(SettlementState.Claimed, SettlementState.Refunded, steps));
    }

    [Fact]
    public void AWalkTooLongForTheBufferIsRefused()
    {
        Span<SettlementState> steps = stackalloc SettlementState[2];
        Assert.Equal(SettlementPath.Unreachable, SettlementPath.Plan(SettlementState.Entered, SettlementState.LostRefundDue, steps));
    }

    private static SettlementState[] Plan(SettlementState from, SettlementState to)
    {
        Span<SettlementState> steps = stackalloc SettlementState[8];
        var count = SettlementPath.Plan(from, to, steps);
        Assert.True(count > 0);
        return steps[..count].ToArray();
    }
}
