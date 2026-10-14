using AutoHouseLottery.Core.Providers.Model;
using static AutoHouseLottery.Tests.HousingFixtures;
using static AutoHouseLottery.Tests.ProviderFixtures;

namespace AutoHouseLottery.Tests;

public sealed class WorldFeedStateTests
{
    private static readonly DateTime now = ObservedAt;

    [Fact]
    public void AWorldIsFetchedOnlyAfterItsSavedCopyWasRead()
    {
        var state = new WorldFeedState();

        Assert.True(state.NeedsCacheRead);
        Assert.False(state.CanFetch(RefreshMode.Active, now));
        state.BeginCacheRead();
        Assert.True(state.Status().Loading);
        state.FinishCacheRead(null, FeedIntakeResult.Ignored(FeedIntakeStatus.FetchFailed));

        Assert.False(state.Status().Loading);
        Assert.True(state.CanFetch(RefreshMode.Active, now));
    }

    [Fact]
    public void AnAnswerTurnedAwayAsStalePutsTheTimerBackAndLeavesNothingInFlight()
    {
        var state = Ready();
        var ticket = new FeedTicket(HomeWorld, 1);
        state.Begin(ticket, now);

        var outcome = state.Finish(ticket, Fetched(World(HomeWorld, Row(0, 0))), FeedIntakeResult.Ignored(FeedIntakeStatus.Stale), now.AddSeconds(2), 0.5d);

        Assert.Equal(FetchOutcome.Superseded, outcome);
        Assert.False(state.Status().Loading);
        Assert.True(state.CanFetch(RefreshMode.Active, now.AddSeconds(3)));
    }

    [Fact]
    public void AnAnswerOvertakenByANewerRequestChangesNothing()
    {
        var state = Ready();
        var older = new FeedTicket(HomeWorld, 1);
        var newer = new FeedTicket(HomeWorld, 2);
        state.Begin(older, now);
        state.Begin(newer, now.AddSeconds(20));

        var outcome = state.Finish(older, Fetched(World(HomeWorld, Row(0, 0))), Applied(), now.AddSeconds(21), 0.5d);

        Assert.Equal(FetchOutcome.Superseded, outcome);
        Assert.True(state.InFlight);
        Assert.False(state.Status().HasData);
    }

    [Fact]
    public void AResponseWithNothingUsableCountsAsAFailureAndKeepsTheSavedCopy()
    {
        var state = Ready(cached: true);
        var ticket = new FeedTicket(HomeWorld, 1);
        state.Begin(ticket, now);

        var outcome = state.Finish(ticket, Fetched(World(HomeWorld)), FeedIntakeResult.Ignored(FeedIntakeStatus.Malformed), now, 0.5d);

        var status = state.Status();
        Assert.Equal(FetchOutcome.Failed, outcome);
        Assert.Equal(FeedFailure.Malformed, status.Failure);
        Assert.True(status.ShowingSavedCopy);
        Assert.Equal(default, status.LiveReceivedUtc);
        Assert.False(state.CanFetch(RefreshMode.Active, now.AddSeconds(30)));
    }

    [Fact]
    public void ASuccessRecordsTheLiveReceiptAndTheRefreshFloor()
    {
        var state = Ready();
        var ticket = new FeedTicket(HomeWorld, 1);
        state.Begin(ticket, now);

        var outcome = state.Finish(ticket, Fetched(World(HomeWorld, Row(0, 0))), Applied(), now, 0.5d);

        var status = state.Status();
        Assert.Equal(FetchOutcome.Applied, outcome);
        Assert.Equal(Receipt.ReceivedUtc, status.LiveReceivedUtc);
        Assert.Equal(now + RefreshCadence.ManualCooldown, status.RefreshAllowedUtc);
    }

    private static WorldFeedState Ready(bool cached = false)
    {
        var state = new WorldFeedState();
        state.BeginCacheRead();
        if (cached)
        {
            state.FinishCacheRead(Fetched(World(HomeWorld, Row(0, 0))), Applied());
            return state;
        }

        state.FinishCacheRead(null, FeedIntakeResult.Ignored(FeedIntakeStatus.FetchFailed));
        return state;
    }

    private static FeedIntakeResult Applied() => new(FeedIntakeStatus.Applied, null, 1, 0, 0);
}
