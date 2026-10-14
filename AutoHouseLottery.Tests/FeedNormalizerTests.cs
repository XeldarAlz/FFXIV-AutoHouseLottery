using System.Linq;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Providers.Model;
using static AutoHouseLottery.Tests.HousingFixtures;
using static AutoHouseLottery.Tests.ProviderFixtures;

namespace AutoHouseLottery.Tests;

public sealed class FeedNormalizerTests
{
    [Fact]
    public void LiveResponseNormalizesEveryRowToItsOneBasedAddress()
    {
        Assert.True(FeedNormalizer.TryParseWorld(SampleBody(), out var detail));

        var result = FeedNormalizer.Normalize(detail, SampleWorld, SampleReceipt, LotterySchedules.Create([]), CalendarUse.Anchor);

        var feed = Assert.IsType<WorldFeed>(result.Feed);
        Assert.Equal(42, feed.Districts.Sum(district => district.Observations.Length));
        Assert.Equal(0, feed.RejectedRows);
        Assert.Equal(5, feed.Districts.Length);
        Assert.All(feed.Districts, district => Assert.Equal(CoverageOutcome.Complete, district.Coverage.Outcome));
        Assert.Contains(feed.Districts[0].Observations, observation => observation.Key == Plot(3, 50, SampleWorld));
        Assert.DoesNotContain(feed.Districts[0].Observations, observation => observation.Key == Plot(2, 49, SampleWorld));
    }

    [Fact]
    public void LiveResponseKeepsSourceTimeApartFromReceiptAndUpstreamTime()
    {
        FeedNormalizer.TryParseWorld(SampleBody(), out var detail);

        var feed = FeedNormalizer.Normalize(detail, SampleWorld, SampleReceipt, LotterySchedules.Create([]), CalendarUse.Anchor).Feed!;

        var observation = feed.Districts[0].Observations[0];
        Assert.Equal(SampleReceived, observation.Provenance.ReceivedUtc);
        Assert.True(observation.Provenance.SourceTimeUtc < SampleUpstream);
        Assert.Equal(SampleUpstream, feed.Districts[0].Coverage.ObservedFromUtc);
        Assert.Equal(SampleReceived, feed.Districts[0].Coverage.ReceivedUtc);
    }

    [Fact]
    public void ZeroBasedIndicesBecomeOneBasedExactlyOnce()
    {
        var feed = Normalize(World(HomeWorld, Row(0, 0), Row(29, 59)));

        var keys = feed.Districts[0].Observations.Select(observation => observation.Key).ToArray();
        Assert.Equal([Plot(1, 1), Plot(30, 60)], keys);
    }

    [Fact]
    public void OutOfRangeAddressesAreRejectedRatherThanClamped()
    {
        var feed = Normalize(World(HomeWorld, Row(30, 0), Row(0, 60), Row(-1, 4), Row(4, 4)));

        var district = feed.Districts[0];
        Assert.Equal([Plot(5, 5)], district.Observations.Select(observation => observation.Key).ToArray());
        Assert.Equal(3, district.RejectedRows);
        Assert.Equal(CoverageOutcome.Partial, district.Coverage.Outcome);
    }

    [Fact]
    public void NullEntriesStayUnknownAndZeroStaysZero()
    {
        var feed = Normalize(World(HomeWorld, Row(0, 0, entries: "null"), Row(0, 1, entries: "0")));

        var observations = feed.Districts[0].Observations;
        Assert.Null(observations[0].EntryCount);
        Assert.Equal(0, observations[1].EntryCount);
        Assert.Equal(FirstCycle, observations[1].Provenance.Cycle);
    }

    [Fact]
    public void ACountWithoutALotteryPhaseIsNotTiedToAnyCycle()
    {
        var feed = Normalize(World(HomeWorld, Row(0, 0, entries: "4", phase: "null", phaseEnd: "null")));

        var observation = feed.Districts[0].Observations[0];
        Assert.Null(observation.EntryCount);
        Assert.Null(observation.Sale);
        Assert.Null(observation.Provenance.Cycle);
        Assert.Equal(PlotBuyers.Both, observation.Buyers);
    }

    [Fact]
    public void UnknownPurchaseFlagsAndInvalidPricesAreNotActionable()
    {
        var feed = Normalize(World(HomeWorld, Row(0, 0, purchase: "0"), Row(0, 1, purchase: "15"), Row(0, 2, price: "0"), Row(0, 3, size: "9")));

        var observations = feed.Districts[0].Observations;
        Assert.Null(observations[0].Buyers);
        Assert.Null(observations[0].Sale);
        Assert.Null(observations[1].Buyers);
        Assert.Null(observations[2].Price);
        Assert.Null(observations[3].Size);
    }

    [Fact]
    public void NegativeCountsAndMissingTimestampsRejectTheRow()
    {
        var feed = Normalize(World(HomeWorld, Row(0, 0, entries: "-1"), Row(0, 1, updated: "null"), Row(0, 2, updated: "0"), Row(0, 3)));

        Assert.Equal([Plot(1, 4)], feed.Districts[0].Observations.Select(observation => observation.Key).ToArray());
        Assert.Equal(3, feed.Districts[0].RejectedRows);
    }

    [Fact]
    public void AResponseWithNothingUsableIsMalformedRatherThanEmpty()
    {
        var allRejected = Problem(World(HomeWorld, Row(30, 0), Row(0, 0, world: OtherWorld)));
        var noDistricts = Problem("""{"id":63,"num_open_plots":0,"districts":[]}"""u8.ToArray());
        var unknownDistricts = Problem("""{"id":63,"districts":[{"id":123,"num_open_plots":0,"open_plots":[]}]}"""u8.ToArray());
        var truncated = Problem("""{"id":63,"districts":[{"id":339,"num_open_plots":2,"open_plots":null}]}"""u8.ToArray());

        Assert.Equal(FeedProblem.Malformed, allRejected);
        Assert.Equal(FeedProblem.Malformed, noDistricts);
        Assert.Equal(FeedProblem.Malformed, unknownDistricts);
        Assert.Equal(FeedProblem.Malformed, truncated);
    }

    [Fact]
    public void ADistrictHonestlyListingNoPlotsIsCompleteAndEmpty()
    {
        var feed = Normalize("""{"id":63,"districts":[{"id":339,"num_open_plots":0,"open_plots":[]}]}"""u8.ToArray());

        Assert.Equal(CoverageOutcome.Complete, feed.Districts[0].Coverage.Outcome);
        Assert.Empty(feed.Districts[0].Observations);
    }

    [Fact]
    public void RejectedRowsCannotMoveTheCalendar()
    {
        var shifted = Seconds(EntryEnd.AddHours(3)).Split('.')[0];
        var schedules = LotterySchedules.Create([]);

        var feed = NormalizeWith(schedules, CalendarUse.Anchor, World(HomeWorld, Row(0, 0), Row(0, 1, world: OtherWorld, phaseEnd: shifted), Row(0, 2, world: OtherWorld, phaseEnd: shifted), Row(31, 3, phaseEnd: shifted)));

        Assert.Equal(ScheduleUpdate.Unchanged, feed.Schedule);
        Assert.True(schedules.TryGet(GameRegion.Global, out var schedule));
        Assert.Equal(LotteryCalendar.Global, schedule);
    }

    [Fact]
    public void OldRowsCannotOutvoteFreshOnes()
    {
        var oldEnd = Seconds(EntryEnd.AddHours(3)).Split('.')[0];
        var oldSeen = Seconds(ObservedAt.AddDays(-6));
        var schedules = LotterySchedules.Create([]);

        var feed = NormalizeWith(schedules, CalendarUse.Anchor, World(HomeWorld,
            Row(0, 0), Row(0, 1),
            Row(1, 0, updated: oldSeen, phaseEnd: oldEnd), Row(1, 1, updated: oldSeen, phaseEnd: oldEnd), Row(1, 2, updated: oldSeen, phaseEnd: oldEnd)));

        Assert.Equal(ScheduleUpdate.Unchanged, feed.Schedule);
        var fresh = feed.Districts[0].Observations.Where(observation => observation.Key.Ward == 1).ToArray();
        Assert.All(fresh, observation => Assert.Equal(PlotSale.LotteryEntry, observation.Sale!.Value.Sale));
    }

    [Fact]
    public void PhaseEndsThatHadPassedWhenTheServiceReadThemDoNotVote()
    {
        var passedEnd = Seconds(ObservedAt.AddMinutes(10)).Split('.')[0];
        var seen = Seconds(ObservedAt.AddDays(-1));
        var schedules = LotterySchedules.Create([]);

        var feed = NormalizeWith(schedules, CalendarUse.Anchor, World(HomeWorld, Row(0, 0, updated: seen, phaseEnd: passedEnd), Row(0, 1, updated: seen, phaseEnd: passedEnd)));

        Assert.Equal(ScheduleUpdate.Unchanged, feed.Schedule);
    }

    [Fact]
    public void AReplayedResponseReadsTheCalendarWithoutMovingIt()
    {
        var shifted = Seconds(EntryEnd.AddHours(3)).Split('.')[0];
        var schedules = LotterySchedules.Create([]);

        var feed = NormalizeWith(schedules, CalendarUse.ReadOnly, World(HomeWorld, Row(0, 0, phaseEnd: shifted), Row(0, 1, phaseEnd: shifted)));

        Assert.Equal(ScheduleUpdate.Unchanged, feed.Schedule);
        Assert.True(schedules.TryGet(GameRegion.Global, out var schedule));
        Assert.Equal(LotteryCalendar.Global, schedule);
        Assert.All(feed.Districts[0].Observations, observation => Assert.Null(observation.Sale));
    }

    [Fact]
    public void FirstComePlotsNeedNoCycleAndPrivateOnlyFlagsMapToPrivate()
    {
        var feed = Normalize(World(HomeWorld, Row(0, 0, purchase: "4", phase: "null", phaseEnd: "null")));

        var observation = feed.Districts[0].Observations[0];
        Assert.Equal(PlotSale.FirstComeOpen, observation.Sale!.Value.Sale);
        Assert.Equal(PlotBuyers.Private, observation.Buyers);
    }

    [Fact]
    public void UnavailablePlotsBelongToTheCycleWhoseEndTheyReport()
    {
        var cycleEnd = Seconds(CycleStart + LotteryCalendar.CycleLength).Split('.')[0];

        var feed = Normalize(World(HomeWorld, Row(0, 0, phase: "3", phaseEnd: cycleEnd)));

        var observation = feed.Districts[0].Observations[0];
        Assert.Equal(PlotSale.Unavailable, observation.Sale!.Value.Sale);
        Assert.Equal(FirstCycle, observation.Provenance.Cycle);
    }

    [Fact]
    public void APhaseEndOffTheLotteryCalendarDropsTheCycleScopedFields()
    {
        var offGrid = Seconds(EntryEnd.AddDays(3)).Split('.')[0];

        var feed = Normalize(World(HomeWorld, Row(0, 0), Row(0, 1), Row(0, 2, phaseEnd: offGrid)));

        var observation = feed.Districts[0].Observations[2];
        Assert.Equal(ScheduleUpdate.Unchanged, feed.Schedule);
        Assert.Null(observation.Sale);
        Assert.Null(observation.EntryCount);
        Assert.Equal(3_187_500, observation.Price);
        Assert.Equal(PlotSale.LotteryEntry, feed.Districts[0].Observations[0].Sale!.Value.Sale);
    }

    [Fact]
    public void PhaseEndsMostRowsAgreeOnMoveTheCalendarAndKeepTheirCycle()
    {
        var shifted = Seconds(EntryEnd.AddHours(3)).Split('.')[0];
        var schedules = LotterySchedules.Create([]);
        Assert.True(FeedNormalizer.TryParseWorld(World(HomeWorld, Row(0, 0, phaseEnd: shifted), Row(0, 1, phaseEnd: shifted)), out var detail));

        var feed = FeedNormalizer.Normalize(detail, HomeWorld, Receipt, schedules, CalendarUse.Anchor).Feed!;

        Assert.Equal(ScheduleUpdate.Reanchored, feed.Schedule);
        Assert.True(schedules.TryGet(GameRegion.Global, out var schedule));
        Assert.Equal(CycleStart.AddHours(3), LotteryCalendar.EntryStartOf(schedule, FirstCycle.Number));
        Assert.All(feed.Districts[0].Observations, observation => Assert.Equal(FirstCycle, observation.Provenance.Cycle));
        Assert.Contains(feed.Positions, position => position.Phase == LotteryPhase.Entry && position.IsVerified);
    }

    [Fact]
    public void ACountThatDisagreesWithTheRowsMakesTheDistrictPartial()
    {
        var feed = Normalize(World(HomeWorld, 3, [Row(0, 0), Row(0, 1)]));

        Assert.Equal(CoverageOutcome.Partial, feed.Districts[0].Coverage.Outcome);
        Assert.Equal(2, feed.Districts[0].Observations.Length);
    }

    [Fact]
    public void DuplicateRowsAndRowsFromAnotherDistrictAreRejected()
    {
        var feed = Normalize(World(HomeWorld, Row(0, 0), Row(0, 0), Row(0, 1, district: HousingDistricts.Goblet)));

        Assert.Single(feed.Districts[0].Observations);
        Assert.Equal(2, feed.Districts[0].RejectedRows);
    }

    [Fact]
    public void AResponseForAnotherWorldOrWithoutDistrictsIsRefused()
    {
        FeedNormalizer.TryParseWorld(World(OtherWorld, Row(0, 0, world: OtherWorld)), out var other);
        FeedNormalizer.TryParseWorld("""{"id":63,"name":"Fixture"}"""u8, out var empty);

        Assert.Equal(FeedProblem.WrongWorld, FeedNormalizer.Normalize(other, HomeWorld, Receipt, LotterySchedules.Create([]), CalendarUse.Anchor).Problem);
        Assert.Equal(FeedProblem.Malformed, FeedNormalizer.Normalize(empty, HomeWorld, Receipt, LotterySchedules.Create([]), CalendarUse.Anchor).Problem);
    }

    [Fact]
    public void ProxyTimesNeverRunAheadOfTheReceipt()
    {
        var received = ObservedAt;

        var ahead = FeedReceipt.From(received, new DateTimeOffset(received.AddMinutes(2)), null);
        var fromAge = FeedReceipt.From(received, null, 185);
        var unknown = FeedReceipt.From(received, null, null);

        Assert.Equal(received, ahead.UpstreamUtc);
        Assert.Equal(received.AddSeconds(-185), fromAge.UpstreamUtc);
        Assert.Equal(received, unknown.UpstreamUtc);
    }

    [Fact]
    public void WorldListSkipsEntriesWithoutAnIdOrName()
    {
        var worlds = FeedNormalizer.ParseWorlds("""[{"id":73,"name":"Adamantoise","datacenter_name":"Aether"},{"id":0,"name":"Zero"},{"name":"Nameless"},null]"""u8);

        Assert.Equal([new FeedWorld(73, "Adamantoise", "Aether")], worlds.ToArray());
    }

    private static WorldFeed Normalize(byte[] body) => NormalizeWith(LotterySchedules.Create([]), CalendarUse.Anchor, body);

    private static WorldFeed NormalizeWith(LotterySchedules schedules, CalendarUse use, byte[] body)
    {
        Assert.True(FeedNormalizer.TryParseWorld(body, out var detail));
        var result = FeedNormalizer.Normalize(detail, HomeWorld, Receipt, schedules, use);
        return Assert.IsType<WorldFeed>(result.Feed);
    }

    private static FeedProblem Problem(byte[] body)
    {
        Assert.True(FeedNormalizer.TryParseWorld(body, out var detail));
        return FeedNormalizer.Normalize(detail, HomeWorld, Receipt, LotterySchedules.Create([]), CalendarUse.Anchor).Problem;
    }
}
