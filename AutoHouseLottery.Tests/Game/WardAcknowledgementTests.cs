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
    private static readonly WardWindowState refreshedAfterPacket = new(Open: true, LayoutValid: true, RowsComplete: true, RequestedWard, LastUpdateSequence: 102);

    [Fact]
    public void AcknowledgesRowsRefreshedAfterThisWardsPacket()
    {
        Assert.Equal(WardAcknowledgement.Acknowledged, Evaluate(refreshedAfterPacket, packetForWard));
        Assert.Equal(WardCorrelation.PacketAndAddon, WardAcknowledgementRule.CorrelationOf(packetForWard));
    }

    [Fact]
    public void StaleRowsUnderTheSameRowCountAreNotAcknowledged()
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
    public void RowsRefreshedBeforeThePacketArrivedAreNotAcknowledged()
    {
        var packetAfterRefresh = packetForWard with { Sequence = 103 };
        Assert.Equal(WardAcknowledgement.Pending, Evaluate(refreshedAfterPacket, packetAfterRefresh));
    }

    [Fact]
    public void APacketFromBeforeTheRequestDoesNotCount()
    {
        var earlierPacket = packetForWard with { Sequence = RequestSequence - 10 };
        Assert.Equal(WardAcknowledgement.Pending, Evaluate(refreshedAfterPacket, earlierPacket));
    }

    [Fact]
    public void APacketForAnotherWardOrDistrictDoesNotCount()
    {
        Assert.Equal(WardAcknowledgement.Pending, Evaluate(refreshedAfterPacket, packetForWard with { WardIndex = OtherWard }));
        Assert.Equal(WardAcknowledgement.Pending, Evaluate(refreshedAfterPacket, packetForWard with { TerritoryTypeId = Goblet }));
        Assert.Equal(WardAcknowledgement.Pending, Evaluate(refreshedAfterPacket, packetForWard with { WorldId = OtherWorld }));
    }

    [Fact]
    public void WithoutThePacketObserverTheWindowAloneAcknowledges()
    {
        var noObserver = new WardPacketState(ObserverActive: false, Present: false, -1, 0, 0, 0);
        Assert.Equal(WardAcknowledgement.Acknowledged, Evaluate(refreshedAfterPacket, noObserver));
        Assert.Equal(WardCorrelation.AddonOnly, WardAcknowledgementRule.CorrelationOf(noObserver));
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
        var partial = refreshedAfterPacket with { RowsComplete = false };
        Assert.Equal(WardAcknowledgement.Pending, Evaluate(partial, packetForWard));
        Assert.Equal(WardAcknowledgement.TimedOut, Evaluate(partial, packetForWard, nowMs: DeadlineMs));
    }

    [Fact]
    public void AnUnexpectedLayoutAfterTheRequestStopsTheReader()
    {
        var mismatched = refreshedAfterPacket with { LayoutValid = false, RowsComplete = false };
        Assert.Equal(WardAcknowledgement.LayoutMismatch, Evaluate(mismatched, packetForWard));
        Assert.Equal(WardAcknowledgement.Pending, Evaluate(mismatched with { LastUpdateSequence = RequestSequence }, packetForWard));
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
    public void ScanCoverageIsCompleteOnlyWhenEveryPlannedWardWasAcknowledged()
    {
        var partial = new WardScanResult([], WardsPlanned: 30, WardScanStop.TimedOut, StoppedAtWard: 0);
        Assert.False(partial.Complete);
        Assert.Equal(WardScanStop.WindowClosed, WardScanResult.StopFor(WardAcknowledgement.WindowClosed));
        Assert.Equal(WardScanStop.ScopeChanged, WardScanResult.StopFor(WardAcknowledgement.ScopeChanged));
    }

    private static WardAcknowledgement Evaluate(in WardWindowState window, in WardPacketState packet, long nowMs = BeforeDeadlineMs)
        => WardAcknowledgementRule.Evaluate(request, window, packet, sameScope, nowMs);
}
