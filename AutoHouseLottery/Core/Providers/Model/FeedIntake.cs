using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Providers.Model;

// The single path from a response, live or replayed from the disk cache, into the plot store. Read parses and normalizes
// and needs no lock on the store; Apply merges. A response that failed, cannot be read or holds nothing usable is turned
// away whole, and so is one overtaken by a newer request for its world, so none of them can empty or overwrite what is
// already known.
internal static class FeedIntake
{
    public static FeedIntakeResult Accept(
        PlotStore store,
        FeedSession session,
        in FeedTicket ticket,
        in FeedFetch fetch,
        LotterySchedules schedules,
        CalendarUse calendar)
    {
        return Apply(store, session, ticket, Read(fetch, ticket.WorldId, schedules, calendar));
    }

    public static FeedRead Read(in FeedFetch fetch, uint worldId, LotterySchedules schedules, CalendarUse calendar)
    {
        if (!fetch.Succeeded)
        {
            return new FeedRead(FeedIntakeStatus.FetchFailed, null);
        }

        if (!FeedNormalizer.TryParseWorld(fetch.Body, out var detail))
        {
            return new FeedRead(FeedIntakeStatus.Malformed, null);
        }

        var result = FeedNormalizer.Normalize(detail, worldId, fetch.Receipt, schedules, calendar);
        return result.Problem switch
        {
            FeedProblem.None => new FeedRead(FeedIntakeStatus.Applied, result.Feed),
            FeedProblem.WrongWorld => new FeedRead(FeedIntakeStatus.WrongWorld, null),
            _ => new FeedRead(FeedIntakeStatus.Malformed, null),
        };
    }

    public static FeedIntakeResult Apply(PlotStore store, FeedSession session, in FeedTicket ticket, in FeedRead read)
    {
        if (read.Feed is not { } feed)
        {
            return FeedIntakeResult.Ignored(read.Status);
        }

        if (!session.IsCurrent(ticket))
        {
            return FeedIntakeResult.Ignored(FeedIntakeStatus.Stale);
        }

        var merged = 0;
        var rejected = feed.RejectedRows;
        var marked = 0;
        var firstRejection = MergeRejection.None;
        for (var index = 0; index < feed.Districts.Length; index++)
        {
            var district = feed.Districts[index];
            var batch = store.Apply(district.Coverage, district.Observations.AsSpan());
            merged += batch.Merged;
            rejected += batch.Rejected;
            marked += batch.MarkedNotReported;
            if (firstRejection == MergeRejection.None)
            {
                firstRejection = batch.BatchRejection != MergeRejection.None ? batch.BatchRejection : batch.FirstRowRejection;
            }
        }

        return new FeedIntakeResult(FeedIntakeStatus.Applied, feed, merged, rejected, marked) { FirstRejection = firstRejection };
    }
}
