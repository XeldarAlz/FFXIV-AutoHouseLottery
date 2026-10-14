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
        Assert.False(LotteryCalendar.TryVerify(LotteryCalendar.Global, LotteryPhase.Entry, CycleStart.AddDays(9), out _));
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
}
