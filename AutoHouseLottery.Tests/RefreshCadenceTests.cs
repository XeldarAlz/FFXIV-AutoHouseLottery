using AutoHouseLottery.Core.Providers.Model;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests;

public sealed class RefreshCadenceTests
{
    private static readonly DateTime now = ObservedAt;

    [Fact]
    public void AWorldNeverFetchedIsDueAtOnceUnlessNothingWatchesIt()
    {
        Assert.True(RefreshCadence.IsDue(default, RefreshMode.Active, now));
        Assert.True(RefreshCadence.IsDue(default, RefreshMode.Idle, now));
        Assert.False(RefreshCadence.IsDue(default, RefreshMode.Paused, now));
    }

    [Fact]
    public void AnOpenWindowRefreshesEveryTwentyMinutesAndSavedSearchesHourly()
    {
        var timer = RefreshCadence.Succeeded(RefreshCadence.Started(default, now), now, null);

        Assert.False(RefreshCadence.IsDue(timer, RefreshMode.Active, now.AddMinutes(19)));
        Assert.True(RefreshCadence.IsDue(timer, RefreshMode.Active, now.AddMinutes(20)));
        Assert.False(RefreshCadence.IsDue(timer, RefreshMode.Idle, now.AddMinutes(59)));
        Assert.True(RefreshCadence.IsDue(timer, RefreshMode.Idle, now.AddMinutes(60)));
    }

    [Fact]
    public void AManualRefreshWaitsOutTheCooldown()
    {
        var timer = RefreshCadence.Succeeded(RefreshCadence.Started(default, now), now, null);

        Assert.Equal(TimeSpan.FromSeconds(15), RefreshCadence.ManualWait(timer, now));
        Assert.Equal(TimeSpan.FromSeconds(5), RefreshCadence.ManualWait(timer, now.AddSeconds(10)));
        Assert.Equal(TimeSpan.Zero, RefreshCadence.ManualWait(timer, now.AddSeconds(15)));
    }

    [Fact]
    public void TheServiceFreshnessWindowDelaysAManualRefreshUpToTheActiveInterval()
    {
        var shortAge = RefreshCadence.Succeeded(RefreshCadence.Started(default, now), now, TimeSpan.FromSeconds(60));
        var hugeAge = RefreshCadence.Succeeded(RefreshCadence.Started(default, now), now, TimeSpan.FromDays(3));

        Assert.Equal(TimeSpan.FromSeconds(60), RefreshCadence.ManualWait(shortAge, now));
        Assert.Equal(RefreshCadence.ActiveInterval, RefreshCadence.ManualWait(hugeAge, now));
    }

    [Fact]
    public void RetryAfterHoldsBothScheduledAndManualRefreshes()
    {
        var timer = RefreshCadence.Failed(RefreshCadence.Started(default, now), now, TimeSpan.FromMinutes(10), 0d);

        Assert.False(RefreshCadence.IsDue(timer, RefreshMode.Active, now.AddMinutes(9)));
        Assert.Equal(TimeSpan.FromMinutes(1), RefreshCadence.ManualWait(timer, now.AddMinutes(9)));
        Assert.True(RefreshCadence.IsDue(timer, RefreshMode.Active, now.AddMinutes(10)));
    }

    [Fact]
    public void AnAbsurdRetryAfterIsCapped()
    {
        var timer = RefreshCadence.Failed(default, now, TimeSpan.FromDays(30), 0d);

        Assert.Equal(now + RefreshCadence.MaximumRetryAfter, timer.RetryAfterUtc);
    }

    [Fact]
    public void BackoffDoublesWithJitterAndStopsAtThirtyMinutes()
    {
        Assert.Equal(TimeSpan.FromSeconds(45), RefreshCadence.Backoff(1, 0d));
        Assert.Equal(TimeSpan.FromSeconds(75), RefreshCadence.Backoff(1, 1d));
        Assert.Equal(TimeSpan.FromMinutes(2), RefreshCadence.Backoff(2, 0.5d));
        Assert.Equal(TimeSpan.FromMinutes(16), RefreshCadence.Backoff(5, 0.5d));
        Assert.Equal(TimeSpan.FromMinutes(30), RefreshCadence.Backoff(6, 0.5d));
        Assert.Equal(TimeSpan.FromMinutes(30), RefreshCadence.Backoff(40, 0.5d));
    }

    [Fact]
    public void AFailedWorldRetriesAfterItsBackoffAndASuccessClearsIt()
    {
        var failed = RefreshCadence.Failed(RefreshCadence.Started(default, now), now, null, 0.5d);

        Assert.False(RefreshCadence.IsDue(failed, RefreshMode.Active, now.AddSeconds(59)));
        Assert.True(RefreshCadence.IsDue(failed, RefreshMode.Active, now.AddMinutes(1)));

        var recovered = RefreshCadence.Succeeded(RefreshCadence.Started(failed, now.AddMinutes(1)), now.AddMinutes(1), null);
        Assert.Equal(0, recovered.ConsecutiveFailures);
        Assert.False(RefreshCadence.IsDue(recovered, RefreshMode.Active, now.AddMinutes(2)));
    }
}
