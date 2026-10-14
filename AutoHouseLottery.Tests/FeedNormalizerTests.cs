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

        var result = FeedNormalizer.Normalize(detail, SampleWorld, SampleReceipt, LotteryCalendar.Global);

        var feed = Assert.IsType<WorldFeed>(result.Feed);
        Assert.Equal(42, feed.ObservationCount);
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

        var feed = FeedNormalizer.Normalize(detail, SampleWorld, SampleReceipt, LotteryCalendar.Global).Feed!;

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
        var feed = Normalize(World(HomeWorld, Row(0, 0, entries: "-1"), Row(0, 1, updated: "null"), Row(0, 2, updated: "0")));

        Assert.Empty(feed.Districts[0].Observations);
        Assert.Equal(3, feed.Districts[0].RejectedRows);
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

        var feed = Normalize(World(HomeWorld, Row(0, 0, phaseEnd: offGrid)));

        var observation = feed.Districts[0].Observations[0];
        Assert.Null(observation.Sale);
        Assert.Null(observation.EntryCount);
        Assert.Equal(3_187_500, observation.Price);
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

        Assert.Equal(FeedProblem.WrongWorld, FeedNormalizer.Normalize(other, HomeWorld, Receipt, LotteryCalendar.Global).Problem);
        Assert.Equal(FeedProblem.Malformed, FeedNormalizer.Normalize(empty, HomeWorld, Receipt, LotteryCalendar.Global).Problem);
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

    private static WorldFeed Normalize(byte[] body)
    {
        Assert.True(FeedNormalizer.TryParseWorld(body, out var detail));
        var result = FeedNormalizer.Normalize(detail, HomeWorld, Receipt, LotteryCalendar.Global);
        return Assert.IsType<WorldFeed>(result.Feed);
    }
}
