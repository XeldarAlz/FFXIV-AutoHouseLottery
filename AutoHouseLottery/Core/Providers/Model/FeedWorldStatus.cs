namespace AutoHouseLottery.Core.Providers.Model;

// What the page can say about one world's listings: whether live data arrived, how old the saved copy it fell back on
// is, and why the last refresh failed. Fetch times live here and never on a plot.
internal readonly record struct FeedWorldStatus(
    bool Loading,
    bool HasData,
    DateTime LiveReceivedUtc,
    DateTime LiveUpstreamUtc,
    DateTime CachedReceivedUtc,
    FeedFailure Failure,
    int HttpStatus)
{
    public bool ShowingSavedCopy => HasData && LiveReceivedUtc == default && CachedReceivedUtc != default;
}
