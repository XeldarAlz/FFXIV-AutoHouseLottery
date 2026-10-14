namespace AutoHouseLottery.Core.Providers.Model;

internal readonly record struct FeedResult(FeedProblem Problem, WorldFeed? Feed)
{
    public static FeedResult Failed(FeedProblem problem) => new(problem, null);
}
