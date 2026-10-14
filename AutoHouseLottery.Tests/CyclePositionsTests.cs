using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Providers.Model;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests;

public sealed class CyclePositionsTests
{
    [Fact]
    public void NewerPositionCoveringTheTimeWinsOverAnOlderOne()
    {
        var entryEnd = CycleStart + LotteryCalendar.EntryPeriod;
        var listing = VerifiedEntryPhase();
        var placard = new CyclePosition(FirstCycle, LotteryPhase.Results, entryEnd.AddHours(-3), entryEnd.AddHours(-3) + LotteryCalendar.ResultsPeriod, 2, CycleEvidence.ServerObserved);
        var now = entryEnd.AddHours(-1);

        Assert.Equal(placard, CycleResolver.Current(LotteryCalendar.Global, [listing, placard], now));
        Assert.Equal(listing, CycleResolver.Current(LotteryCalendar.Global, [placard, listing], now));
    }

    [Fact]
    public void KeptPositionsAreDeduplicatedAndTheOldestGoesPastTheLimit()
    {
        var kept = new List<CyclePosition>();
        var first = VerifiedEntryPhase();
        CyclePositions.Remember(kept, [first, first]);
        Assert.Single(kept);

        for (var index = 1; index <= CyclePositions.MaximumKept; index++)
        {
            CyclePositions.Remember(kept, [first with { ScheduleRevision = index + 1 }]);
        }

        Assert.Equal(CyclePositions.MaximumKept, kept.Count);
        Assert.DoesNotContain(first, kept);
        Assert.Equal(CyclePositions.MaximumKept + 1, kept[^1].ScheduleRevision);
    }

    [Theory]
    [InlineData((byte)ScheduleUpdate.Established, true)]
    [InlineData((byte)ScheduleUpdate.Reanchored, true)]
    [InlineData((byte)ScheduleUpdate.Unchanged, false)]
    [InlineData((byte)ScheduleUpdate.Rejected, false)]
    public void OnlyAnEstablishedOrMovedCalendarIsWrittenOut(byte update, bool persists)
    {
        Assert.Equal(persists, CyclePositions.Persists((ScheduleUpdate)update));
    }
}
