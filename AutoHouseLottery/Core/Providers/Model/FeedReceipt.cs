namespace AutoHouseLottery.Core.Providers.Model;

// When this plugin received a response, and when the service behind the proxy last read the upstream listing. Neither
// is the time a plot was observed; each plot carries its own source time.
internal readonly record struct FeedReceipt(DateTime ReceivedUtc, DateTime UpstreamUtc)
{
    private const int ClockToleranceMinutes = 5;

    private static readonly TimeSpan clockTolerance = TimeSpan.FromMinutes(ClockToleranceMinutes);

    public static FeedReceipt From(DateTime receivedUtc, DateTimeOffset? upstreamFetched, int? proxyAgeSeconds)
    {
        if (upstreamFetched is { } fetched && fetched.UtcDateTime <= receivedUtc + clockTolerance && fetched.UtcDateTime > DateTime.UnixEpoch)
        {
            var upstream = fetched.UtcDateTime;
            return new FeedReceipt(receivedUtc, upstream > receivedUtc ? receivedUtc : upstream);
        }

        if (proxyAgeSeconds is { } age && age >= 0)
        {
            return new FeedReceipt(receivedUtc, receivedUtc - TimeSpan.FromSeconds(age));
        }

        return new FeedReceipt(receivedUtc, receivedUtc);
    }
}
