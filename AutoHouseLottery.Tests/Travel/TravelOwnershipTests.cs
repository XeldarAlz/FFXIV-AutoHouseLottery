using AutoHouseLottery.Core.Travel.Model;

namespace AutoHouseLottery.Tests.Travel;

public sealed class TravelOwnershipTests
{
    private const long RequestedAtMs = 1_000;
    private const long WithinGraceMs = RequestedAtMs + TravelOwnership.StartGraceMs - 1;
    private const long AfterGraceMs = RequestedAtMs + TravelOwnership.StartGraceMs;

    [Fact]
    public void BusyAfterTheRequestIsOurWorkAndMayBeAborted()
    {
        var ownership = new TravelOwnership(RequestedAtMs);
        ownership.Observe(dependencyBusy: true, RequestedAtMs + 100);
        Assert.Equal(TravelPhase.Running, ownership.Phase);
        Assert.True(ownership.MayAbort(dependencyBusy: true));
    }

    [Fact]
    public void BusyAgainAfterOurWorkStoppedBelongsToSomebodyElse()
    {
        var ownership = new TravelOwnership(RequestedAtMs);
        ownership.Observe(dependencyBusy: true, RequestedAtMs + 100);
        ownership.Observe(dependencyBusy: false, RequestedAtMs + 200);
        Assert.Equal(TravelPhase.Finished, ownership.Phase);
        ownership.Observe(dependencyBusy: true, RequestedAtMs + 300);
        Assert.Equal(TravelPhase.TakenOver, ownership.Phase);
        Assert.False(ownership.MayAbort(dependencyBusy: true));
    }

    [Fact]
    public void ARequestThatNeverMadeLifestreamBusyIsNotOurs()
    {
        var ownership = new TravelOwnership(RequestedAtMs);
        ownership.Observe(dependencyBusy: false, WithinGraceMs);
        Assert.Equal(TravelPhase.Requested, ownership.Phase);
        ownership.Observe(dependencyBusy: false, AfterGraceMs);
        Assert.Equal(TravelPhase.NeverStarted, ownership.Phase);
        ownership.Observe(dependencyBusy: true, AfterGraceMs + 1);
        Assert.Equal(TravelPhase.TakenOver, ownership.Phase);
        Assert.False(ownership.MayAbort(dependencyBusy: true));
    }

    [Fact]
    public void NothingIsAbortedBeforeOurWorkIsSeenRunning()
    {
        Assert.False(new TravelOwnership(RequestedAtMs).MayAbort(dependencyBusy: true));
    }

    [Fact]
    public void IdleIsNotArrival()
    {
        Assert.Equal(TravelState.Travelling, Evaluate(TravelPhase.Finished, ArrivalVerdict.Unknown));
        Assert.Equal(TravelState.Arrived, Evaluate(TravelPhase.Finished, ArrivalVerdict.Arrived));
    }

    [Fact]
    public void ArrivalWhileLifestreamStillRunsWaitsForItToFinish()
    {
        Assert.Equal(TravelState.Travelling, Evaluate(TravelPhase.Running, ArrivalVerdict.Arrived));
    }

    [Fact]
    public void IdleSomewhereElseIsTheWrongDestination()
    {
        Assert.Equal(TravelState.WrongDestination, Evaluate(TravelPhase.Finished, ArrivalVerdict.WrongWard));
        Assert.Equal(TravelState.WrongDestination, Evaluate(TravelPhase.Finished, ArrivalVerdict.WrongDivision));
    }

    [Fact]
    public void ARequestThatNeverStartedIsFineWhenAlreadyThere()
    {
        Assert.Equal(TravelState.Arrived, Evaluate(TravelPhase.NeverStarted, ArrivalVerdict.Arrived));
        Assert.Equal(TravelState.NotStarted, Evaluate(TravelPhase.NeverStarted, ArrivalVerdict.WrongDistrict));
    }

    [Fact]
    public void TakeoverCancelAndDeadlineEndTheTravel()
    {
        Assert.Equal(TravelState.TakenOver, Evaluate(TravelPhase.TakenOver, ArrivalVerdict.Arrived));
        Assert.Equal(TravelState.Cancelled, TravelProgressRule.Evaluate(TravelPhase.Running, ArrivalVerdict.Unknown, cancelled: true, pastDeadline: false));
        Assert.Equal(TravelState.TimedOut, TravelProgressRule.Evaluate(TravelPhase.Running, ArrivalVerdict.Unknown, cancelled: false, pastDeadline: true));
        Assert.Equal(TravelState.TimedOut, TravelProgressRule.Evaluate(TravelPhase.Finished, ArrivalVerdict.Unknown, cancelled: false, pastDeadline: true));
    }

    private static TravelState Evaluate(TravelPhase phase, ArrivalVerdict verdict)
        => TravelProgressRule.Evaluate(phase, verdict, cancelled: false, pastDeadline: false);
}
