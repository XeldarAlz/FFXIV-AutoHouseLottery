using System.Text;
using AutoHouseLottery.Core.Providers.Model;
using static AutoHouseLottery.Tests.HousingFixtures;
using static AutoHouseLottery.Tests.ProviderFixtures;

namespace AutoHouseLottery.Tests;

public sealed class FeedCacheRulesTests
{
    private static readonly DateTime now = Receipt.ReceivedUtc.AddHours(2);

    [Fact]
    public void ASavedResponseComesBackWithItsOwnReceipt()
    {
        var body = World(HomeWorld, Row(0, 0));

        Assert.True(FeedCacheRules.TryAccept(FeedCacheRules.Envelope(HomeWorld, body, Receipt), HomeWorld, FeedCacheRules.MaximumAge, now, out var fetch));

        Assert.Equal(Receipt, fetch.Receipt);
        Assert.Equal(body, fetch.Body);
        Assert.True(fetch.Succeeded);
    }

    [Fact]
    public void AnotherWorldsOrAnOldOrAFutureCopyIsIgnored()
    {
        var envelope = FeedCacheRules.Envelope(HomeWorld, World(HomeWorld, Row(0, 0)), Receipt);

        Assert.False(FeedCacheRules.TryAccept(envelope, OtherWorld, FeedCacheRules.MaximumAge, now, out _));
        Assert.False(FeedCacheRules.TryAccept(envelope, HomeWorld, FeedCacheRules.MaximumAge, Receipt.ReceivedUtc + FeedCacheRules.MaximumAge + TimeSpan.FromMinutes(1), out _));
        Assert.False(FeedCacheRules.TryAccept(envelope, HomeWorld, FeedCacheRules.MaximumAge, Receipt.ReceivedUtc.AddMinutes(-1), out _));
    }

    [Fact]
    public void ADamagedOrForeignFileIsIgnored()
    {
        Assert.False(FeedCacheRules.TryAccept("{broken"u8, HomeWorld, FeedCacheRules.MaximumAge, now, out _));
        Assert.False(FeedCacheRules.TryAccept(Encoding.UTF8.GetBytes("""{"schema":99,"worldId":63,"receivedUtc":"2026-10-14T22:00:00Z","upstreamUtc":"2026-10-14T21:50:00Z","body":"{}"}"""), HomeWorld, FeedCacheRules.MaximumAge, now, out _));
        Assert.False(FeedCacheRules.TryAccept(Encoding.UTF8.GetBytes("""{"schema":1,"worldId":63,"body":"{}"}"""), HomeWorld, FeedCacheRules.MaximumAge, now, out _));
    }
}
