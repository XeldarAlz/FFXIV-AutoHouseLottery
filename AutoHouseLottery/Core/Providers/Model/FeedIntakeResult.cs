namespace AutoHouseLottery.Core.Providers.Model;

internal readonly record struct FeedIntakeResult(FeedIntakeStatus Status, WorldFeed? Feed, int Merged, int Rejected, int MarkedNotReported)
{
    public bool Applied => Status == FeedIntakeStatus.Applied;

    public static FeedIntakeResult Ignored(FeedIntakeStatus status) => new(status, null, 0, 0, 0);
}
