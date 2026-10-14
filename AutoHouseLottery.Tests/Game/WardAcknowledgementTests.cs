using AutoHouseLottery.Core.Game.Model;

namespace AutoHouseLottery.Tests.Game;

public sealed class WardAcknowledgementTests
{
    private const int RequestedWard = 4;
    private const int OtherWard = 3;
    private const uint Mist = 339;
    private const uint Goblet = 341;
    private const uint World = 73;
    private const uint OtherWorld = 74;
    private const long RequestSequence = 100;
    private const long DeadlineMs = 10_000;
    private const long BeforeDeadlineMs = 5_000;

    private static readonly WardRequest request = new(RequestedWard, Mist, World, RequestSequence, DeadlineMs);
    private static readonly WardScope sameScope = new(Mist, World);
    private static readonly WardPacketState packetForWard = new(ObserverActive: true, Present: true, RequestedWard, Mist, World, Sequence: 101);
    private static readonly WardWindowHeader refreshedAfterPacket = new(Open: true, HeaderValid: true, RequestedWard, LastUpdateSequence: 102);

    [Fact]
    public void RowsRefreshedAfterThisWardsPacketAreReadAndAcknowledged()
    {
        Assert.Equal(WardAcknowledgement.RowsDue, Evaluate(refreshedAfterPacket, packetForWard));
        Assert.Equal(WardAcknowledgement.Acknowledged, WardAcknowledgementRule.AfterRows(WardReadStatus.Read, request, BeforeDeadlineMs));
    }

    [Fact]
    public void StaleRowsUnderTheSameRowCountAreNotRead()
    {
        var notRefreshed = refreshedAfterPacket with { LastUpdateSequence = RequestSequence - 1 };
        Assert.Equal(WardAcknowledgement.Pending, Evaluate(notRefreshed, packetForWard));
    }

    [Fact]
    public void APageStillShowingAnotherWardIsPending()
    {
        Assert.Equal(WardAcknowledgement.Pending, Evaluate(refreshedAfterPacket with { ShownWardIndex = OtherWard }, packetForWard));
    }

    [Fact]
    public void RowsRefreshedBeforeThePacketArrivedAreNotRead()
    {
        Assert.Equal(WardAcknowledgement.Pending, Evaluate(refreshedAfterPacket, packetForWard with { Sequence = 103 }));
    }

    [Fact]
    public void APacketFromBeforeTheRequestDoesNotCount()
    {
        Assert.Equal(WardAcknowledgement.Pending, Evaluate(refreshedAfterPacket, packetForWard with { Sequence = RequestSequence - 10 }));
    }

    [Fact]
    public void APacketForAnotherWardOrDistrictDoesNotCount()
    {
        Assert.Equal(WardAcknowledgement.Pending, Evaluate(refreshedAfterPacket, packetForWard with { WardIndex = OtherWard }));
        Assert.Equal(WardAcknowledgement.Pending, Evaluate(refreshedAfterPacket, packetForWard with { TerritoryTypeId = Goblet }));
        Assert.Equal(WardAcknowledgement.Pending, Evaluate(refreshedAfterPacket, packetForWard with { WorldId = OtherWorld }));
    }

    [Fact]
    public void WithoutThePacketObserverNothingIsAcknowledged()
    {
        var noObserver = new WardPacketState(ObserverActive: false, Present: false, -1, 0, 0, 0);
        Assert.Equal(WardAcknowledgement.NoPacketObserver, Evaluate(refreshedAfterPacket, noObserver));
        Assert.Equal(WardScanStop.NoPacketObserver, WardScanResult.StopFor(WardAcknowledgement.NoPacketObserver));
    }

    [Fact]
    public void AnActiveObserverThatSawNoPacketKeepsItPending()
    {
        var noPacketYet = new WardPacketState(ObserverActive: true, Present: false, -1, 0, 0, 0);
        Assert.Equal(WardAcknowledgement.Pending, Evaluate(refreshedAfterPacket, noPacketYet));
    }

    [Fact]
    public void PartiallyFilledRowsArePendingUntilTheDeadline()
    {
        Assert.Equal(WardAcknowledgement.Pending, WardAcknowledgementRule.AfterRows(WardReadStatus.Incomplete, request, BeforeDeadlineMs));
        Assert.Equal(WardAcknowledgement.TimedOut, WardAcknowledgementRule.AfterRows(WardReadStatus.Incomplete, request, DeadlineMs));
    }

    [Fact]
    public void AnUnexpectedLayoutAfterTheRequestStopsTheReader()
    {
        var mismatched = refreshedAfterPacket with { HeaderValid = false };
        Assert.Equal(WardAcknowledgement.LayoutMismatch, Evaluate(mismatched, packetForWard));
        Assert.Equal(WardAcknowledgement.Pending, Evaluate(mismatched with { LastUpdateSequence = RequestSequence }, packetForWard));
        Assert.Equal(WardAcknowledgement.LayoutMismatch, WardAcknowledgementRule.AfterRows(WardReadStatus.LayoutMismatch, request, BeforeDeadlineMs));
    }

    [Fact]
    public void AClosedWindowEndsTheRequest()
    {
        Assert.Equal(WardAcknowledgement.WindowClosed, Evaluate(refreshedAfterPacket with { Open = false }, packetForWard));
    }

    [Fact]
    public void SwitchingDistrictOrWorldMidScanInvalidatesTheRequest()
    {
        Assert.Equal(WardAcknowledgement.ScopeChanged, WardAcknowledgementRule.Evaluate(request, refreshedAfterPacket, packetForWard, new WardScope(Goblet, World), BeforeDeadlineMs));
        Assert.Equal(WardAcknowledgement.ScopeChanged, WardAcknowledgementRule.Evaluate(request, refreshedAfterPacket, packetForWard, new WardScope(Mist, OtherWorld), BeforeDeadlineMs));
    }

    [Fact]
    public void AnUnansweredRequestTimesOut()
    {
        var notRefreshed = refreshedAfterPacket with { LastUpdateSequence = RequestSequence };
        Assert.Equal(WardAcknowledgement.TimedOut, Evaluate(notRefreshed, packetForWard, nowMs: DeadlineMs + 1));
    }

    [Fact]
    public void AScanThatStoppedEarlyIsNeverComplete()
    {
        Assert.False(new WardScanResult([], WardsPlanned: 30, WardScanStop.NoPacketObserver, StoppedAtWard: 0).Complete);
        Assert.False(new WardScanResult([], WardsPlanned: 30, WardScanStop.Completed, StoppedAtWard: null).Complete);
    }

    private static WardAcknowledgement Evaluate(in WardWindowHeader window, in WardPacketState packet, long nowMs = BeforeDeadlineMs)
        => WardAcknowledgementRule.Evaluate(request, window, packet, sameScope, nowMs);
}
