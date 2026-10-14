namespace AutoHouseLottery.Core.Providers.Model;

internal readonly record struct FeedFetch(FetchStatus Status, byte[]? Body, int HttpStatus, TimeSpan? RetryAfter, TimeSpan? MaxAge, FeedReceipt Receipt)
{
    public bool Succeeded => Status == FetchStatus.Success && Body is not null;
}
