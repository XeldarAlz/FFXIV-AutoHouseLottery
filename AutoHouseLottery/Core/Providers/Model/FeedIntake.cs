using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Providers.Model;

// The single path from a response, live or replayed from the disk cache, into the plot store. A response that failed,
// arrived late, or cannot be read is turned away whole, so it can never empty or overwrite what is already known.
internal static class FeedIntake
{
    public static FeedIntakeResult Accept(
        PlotStore store,
        FeedSession session,
        in FeedTicket ticket,
        ReadOnlySpan<uint> scope,
        in FeedFetch fetch,
        in LotterySchedule schedule)
    {
        if (!fetch.Succeeded)
        {
            return FeedIntakeResult.Ignored(FeedIntakeStatus.FetchFailed);
        }

        if (!session.IsCurrent(ticket, scope))
        {
            return FeedIntakeResult.Ignored(FeedIntakeStatus.Stale);
        }

        if (!FeedNormalizer.TryParseWorld(fetch.Body, out var detail))
        {
            return FeedIntakeResult.Ignored(FeedIntakeStatus.Malformed);
        }

        var result = FeedNormalizer.Normalize(detail, ticket.WorldId, fetch.Receipt, schedule);
        return result.Problem switch
        {
            FeedProblem.None => Apply(store, result.Feed!),
            FeedProblem.WrongWorld => FeedIntakeResult.Ignored(FeedIntakeStatus.WrongWorld),
            _ => FeedIntakeResult.Ignored(FeedIntakeStatus.Malformed),
        };
    }

    private static FeedIntakeResult Apply(PlotStore store, WorldFeed feed)
    {
        var merged = 0;
        var rejected = feed.RejectedRows;
        var marked = 0;
        for (var index = 0; index < feed.Districts.Length; index++)
        {
            var district = feed.Districts[index];
            var batch = store.Apply(district.Coverage, district.Observations.AsSpan());
            merged += batch.Merged;
            rejected += batch.Rejected;
            marked += batch.MarkedNotReported;
        }

        return new FeedIntakeResult(FeedIntakeStatus.Applied, feed, merged, rejected, marked);
    }
}
