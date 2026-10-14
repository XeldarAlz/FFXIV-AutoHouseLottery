namespace AutoHouseLottery.Core.Providers.Model;

// One world's refresh state. Only the newest request for a world may settle it: an answer overtaken by a newer request
// changes nothing, and a turned-away answer to the newest request puts the timer back as it was, so the world is never
// left looking busy with nothing in flight.
internal sealed class WorldFeedState
{
    private RefreshTimer timer;
    private RefreshTimer timerBeforeFetch;
    private long pendingTicket;
    private bool cacheRead;
    private bool cacheReading;
    private bool hasData;
    private DateTime liveReceivedUtc;
    private DateTime liveUpstreamUtc;
    private DateTime cachedReceivedUtc;
    private FeedFailure failure;
    private int httpStatus;

    public RefreshTimer Timer => timer;

    public bool InFlight => pendingTicket != 0;

    public bool NeedsCacheRead => !cacheRead && !cacheReading;

    public bool CanFetch(RefreshMode mode, DateTime nowUtc) => cacheRead && !InFlight && RefreshCadence.IsDue(timer, mode, nowUtc);

    public bool CanRefreshNow(DateTime nowUtc) => cacheRead && !InFlight && RefreshCadence.ManualWait(timer, nowUtc) == TimeSpan.Zero;

    public void BeginCacheRead() => cacheReading = true;

    public void FinishCacheRead(in FeedFetch? cached, in FeedIntakeResult result)
    {
        cacheRead = true;
        cacheReading = false;
        if (cached is { } fetch && result.Applied)
        {
            hasData = true;
            cachedReceivedUtc = fetch.Receipt.ReceivedUtc;
        }
    }

    public void Begin(in FeedTicket ticket, DateTime nowUtc)
    {
        timerBeforeFetch = timer;
        timer = RefreshCadence.Started(timer, nowUtc);
        pendingTicket = ticket.Number;
    }

    public FetchOutcome Finish(in FeedTicket ticket, in FeedFetch fetch, in FeedIntakeResult result, DateTime nowUtc, double jitter)
    {
        if (ticket.Number != pendingTicket)
        {
            return FetchOutcome.Superseded;
        }

        pendingTicket = 0;
        if (result.Applied)
        {
            timer = RefreshCadence.Succeeded(timer, nowUtc, fetch.MaxAge);
            hasData = true;
            liveReceivedUtc = fetch.Receipt.ReceivedUtc;
            liveUpstreamUtc = fetch.Receipt.UpstreamUtc;
            failure = FeedFailure.None;
            httpStatus = fetch.HttpStatus;
            return FetchOutcome.Applied;
        }

        if (result.Status == FeedIntakeStatus.Stale)
        {
            timer = timerBeforeFetch;
            return FetchOutcome.Superseded;
        }

        timer = RefreshCadence.Failed(timer, nowUtc, fetch.RetryAfter, jitter);
        failure = FailureOf(fetch.Status);
        httpStatus = fetch.HttpStatus;
        return FetchOutcome.Failed;
    }

    public DateTime RetryAt => timer.RetryAfterUtc > timer.BackoffUntilUtc ? timer.RetryAfterUtc : timer.BackoffUntilUtc;

    public FeedWorldStatus Status() => new(
        InFlight || cacheReading,
        hasData,
        liveReceivedUtc,
        liveUpstreamUtc,
        cachedReceivedUtc,
        failure,
        httpStatus,
        RefreshCadence.ManualAllowedAt(timer));

    private static FeedFailure FailureOf(FetchStatus status) => status switch
    {
        FetchStatus.Success => FeedFailure.Malformed,
        FetchStatus.RateLimited => FeedFailure.RateLimited,
        FetchStatus.HttpError => FeedFailure.HttpError,
        FetchStatus.Oversized => FeedFailure.Oversized,
        FetchStatus.TimedOut => FeedFailure.TimedOut,
        _ => FeedFailure.Unreachable,
    };
}
