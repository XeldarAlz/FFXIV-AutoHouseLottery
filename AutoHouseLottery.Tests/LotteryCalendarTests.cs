using AutoHouseLottery.Core.Housing.Model;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests;

public sealed class LotteryCalendarTests
{
    [Fact]
    public void AnchorOpensTheEntryPeriodOfTheFirstCycle()
    {
        var position = LotteryCalendar.Estimate(LotteryCalendar.Global, CycleStart);

        Assert.Equal(FirstCycle, position.Cycle);
        Assert.Equal(LotteryPhase.Entry, position.Phase);
        Assert.Equal(CycleStart, position.PhaseStartUtc);
        Assert.Equal(CycleStart.AddDays(5), position.PhaseEndUtc);
    }

    [Fact]
    public void ResultsFollowFiveEntryDaysAndLastFourDays()
    {
        var lastEntryMoment = LotteryCalendar.Estimate(LotteryCalendar.Global, CycleStart.AddDays(5).AddTicks(-1));
        var results = LotteryCalendar.Estimate(LotteryCalendar.Global, CycleStart.AddDays(5));

        Assert.Equal(LotteryPhase.Entry, lastEntryMoment.Phase);
        Assert.Equal(LotteryPhase.Results, results.Phase);
        Assert.Equal(FirstCycle, results.Cycle);
        Assert.Equal(CycleStart.AddDays(9), results.PhaseEndUtc);
    }

    [Fact]
    public void NextCycleStartsNineDaysAfterTheAnchor()
    {
        var position = LotteryCalendar.Estimate(LotteryCalendar.Global, CycleStart.AddDays(9));

        Assert.Equal(SecondCycle, position.Cycle);
        Assert.Equal(LotteryPhase.Entry, position.Phase);
    }

    [Fact]
    public void MomentsBeforeTheAnchorBelongToTheEarlierCycle()
    {
        var position = LotteryCalendar.Estimate(LotteryCalendar.Global, CycleStart.AddTicks(-1));

        Assert.Equal(new CycleKey(GameRegion.Global, 0), position.Cycle);
        Assert.Equal(LotteryPhase.Results, position.Phase);
        Assert.Equal(CycleStart, position.PhaseEndUtc);
    }

    [Fact]
    public void AnEstimateIsNeverVerified()
    {
        Assert.False(LotteryCalendar.Estimate(LotteryCalendar.Global, CycleStart.AddDays(1)).IsVerified);
    }

    [Fact]
    public void ServerPhaseEndVerifiesTheCycleDespiteAMaintenanceDelay()
    {
        var delayedEnd = CycleStart.AddDays(14).AddHours(3);

        Assert.True(LotteryCalendar.TryVerify(LotteryCalendar.Global, LotteryPhase.Entry, delayedEnd, out var position));
        Assert.True(position.IsVerified);
        Assert.Equal(SecondCycle, position.Cycle);
        Assert.Equal(delayedEnd, position.PhaseEndUtc);
    }

    [Fact]
    public void PhaseEndFarFromTheScheduleIsNotVerified()
    {
        Assert.False(LotteryCalendar.TryVerify(LotteryCalendar.Global, LotteryPhase.Entry, CycleStart.AddDays(9).AddHours(12), out _));
        Assert.False(LotteryCalendar.TryVerify(LotteryCalendar.Global, LotteryPhase.Unknown, CycleStart.AddDays(5), out _));
    }

    [Fact]
    public void CorrectingTheScheduleKeepsAnEnteredCycleWithoutANewSlot()
    {
        var entered = LotteryCalendar.Estimate(LotteryCalendar.Global, CycleStart.AddDays(1)).Cycle;
        Assert.True(LotteryCalendar.TryVerify(LotteryCalendar.Global, LotteryPhase.Results, CycleStart.AddDays(9).AddHours(12), out var delayed));

        var corrected = LotteryCalendar.Correct(LotteryCalendar.Global, delayed);
        var moment = CycleStart.AddDays(9).AddHours(6);

        Assert.Equal(SecondCycle, LotteryCalendar.Estimate(LotteryCalendar.Global, moment).Cycle);
        Assert.Equal(2, corrected.Revision);
        var correctedPosition = LotteryCalendar.Estimate(corrected, moment);
        Assert.Equal(entered, correctedPosition.Cycle);
        Assert.Equal(LotteryPhase.Results, correctedPosition.Phase);
        Assert.Equal(entered, LotteryCalendar.Estimate(corrected, CycleStart.AddDays(1)).Cycle);
    }

    [Fact]
    public void AnchorMatchesTheHousingFeedObservedOnWorld404()
    {
        var entryPhaseEnd = DateTimeOffset.FromUnixTimeSeconds(1791644400).UtcDateTime;
        var resultsPhaseEnd = DateTimeOffset.FromUnixTimeSeconds(1791990000).UtcDateTime;

        Assert.True(LotteryCalendar.TryVerify(LotteryCalendar.Global, LotteryPhase.Entry, entryPhaseEnd, out var entry));
        Assert.True(LotteryCalendar.TryVerify(LotteryCalendar.Global, LotteryPhase.Results, resultsPhaseEnd, out var results));

        Assert.Equal(new DateTime(2026, 10, 10, 15, 0, 0, DateTimeKind.Utc), entryPhaseEnd);
        Assert.Equal(CycleStart, resultsPhaseEnd);
        Assert.Equal(entry.Cycle, results.Cycle);
        Assert.Equal(FirstCycle.Number - 1, results.Cycle.Number);
        Assert.Equal(results.PhaseEndUtc, LotteryCalendar.Estimate(LotteryCalendar.Global, CycleStart).PhaseStartUtc);
    }

    [Fact]
    public void FirstObservationEstablishesARegionWithoutADefaultGrid()
    {
        var schedules = LotterySchedules.Create([]);
        var chinaResultsEnd = CycleStart.AddHours(-7);

        Assert.False(schedules.TryGet(GameRegion.China, out _));
        Assert.Equal(ScheduleUpdate.Established, schedules.Observe(GameRegion.China, LotteryPhase.Results, chinaResultsEnd, out var position));

        Assert.True(position.IsVerified);
        Assert.True(schedules.TryGet(GameRegion.China, out var china));
        Assert.Equal(chinaResultsEnd, LotteryCalendar.Estimate(china, chinaResultsEnd.AddTicks(-1)).PhaseEndUtc);
        Assert.Equal(2, schedules.All.Length);
    }

    [Fact]
    public void ObservationOnTheGridLeavesTheScheduleUnchanged()
    {
        var schedules = LotterySchedules.Create([]);

        Assert.Equal(ScheduleUpdate.Unchanged, schedules.Observe(GameRegion.Global, LotteryPhase.Entry, CycleStart.AddDays(14), out var position));
        Assert.Equal(SecondCycle, position.Cycle);
        Assert.True(schedules.TryGet(GameRegion.Global, out var global));
        Assert.Equal(LotteryCalendar.Global, global);
    }

    [Fact]
    public void RepeatedDelaysReanchorInsteadOfAccumulatingIntoALockout()
    {
        var schedules = LotterySchedules.Create([]);
        var phaseEnd = CycleStart.AddDays(5);
        for (var cycleIndex = 0; cycleIndex < 6; cycleIndex++)
        {
            phaseEnd = phaseEnd.AddDays(LotteryCalendar.CycleDays).AddDays(1);

            var update = schedules.Observe(GameRegion.Global, LotteryPhase.Entry, phaseEnd, out var position);

            Assert.Equal(ScheduleUpdate.Reanchored, update);
            Assert.Equal(FirstCycle.Number + cycleIndex + 1, position.Cycle.Number);
        }

        Assert.True(schedules.TryGet(GameRegion.Global, out var corrected));
        Assert.Equal(phaseEnd - LotteryCalendar.EntryPeriod, corrected.AnchorEntryStartUtc);
    }

    [Fact]
    public void PersistedScheduleReplacesTheDefault()
    {
        var corrected = LotteryCalendar.Global with { AnchorEntryStartUtc = CycleStart.AddHours(6), Revision = 3 };

        var schedules = LotterySchedules.Create([corrected]);

        Assert.True(schedules.TryGet(GameRegion.Global, out var global));
        Assert.Equal(corrected, global);
    }
}
