using System.Collections.Immutable;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Providers.Model;

internal sealed record WorldFeed(uint WorldId, FeedReceipt Receipt, ImmutableArray<DistrictFeed> Districts, ImmutableArray<CyclePosition> Positions, ScheduleUpdate Schedule)
{
    public int ObservationCount
    {
        get
        {
            var count = 0;
            for (var index = 0; index < Districts.Length; index++)
            {
                count += Districts[index].Observations.Length;
            }

            return count;
        }
    }

    public int RejectedRows
    {
        get
        {
            var count = 0;
            for (var index = 0; index < Districts.Length; index++)
            {
                count += Districts[index].RejectedRows;
            }

            return count;
        }
    }
}
