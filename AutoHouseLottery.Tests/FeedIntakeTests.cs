using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Providers.Model;
using static AutoHouseLottery.Tests.HousingFixtures;
using static AutoHouseLottery.Tests.ProviderFixtures;

namespace AutoHouseLottery.Tests;

public sealed class FeedIntakeTests
{
    private const long Limit = 1_024;

    private static readonly PlotKey kept = Plot(1, 1);
    private static readonly PlotKey dropped = Plot(1, 2);

    [Fact]
    public void ACompleteRefreshMarksADroppedPlotNotReported()
    {
        var (store, session) = Primed();
        var later = new FeedReceipt(Receipt.ReceivedUtc.AddHours(1), Receipt.UpstreamUtc.AddHours(1));

        var result = Accept(store, session, Fetched(World(HomeWorld, Row(0, 0)), later));

        Assert.True(result.Applied);
        Assert.Equal(1, result.MarkedNotReported);
        Assert.Equal(PlotAvailability.NotReported, store.Plots[dropped].AvailabilityAt(VerifiedEntryPhase()));
    }

    [Fact]
    public void AFailedResponseKeepsEveryPlot()
    {
        var (store, session) = Primed();

        var result = Accept(store, session, new FeedFetch(FetchStatus.HttpError, null, 503, null, null, Receipt));

        Assert.Equal(FeedIntakeStatus.FetchFailed, result.Status);
        AssertBothStillListed(store);
    }

    [Fact]
    public void AMalformedResponseKeepsEveryPlot()
    {
        var (store, session) = Primed();

        var result = Accept(store, session, Fetched(Encoding.UTF8.GetBytes("""{"id":63,"districts":[{"id":339,"open_plots":[{"ward_number":"one"}]}]}""")));

        Assert.Equal(FeedIntakeStatus.Malformed, result.Status);
        AssertBothStillListed(store);
    }

    [Fact]
    public void APartialResponseNeverMarksTheMissingPlots()
    {
        var (store, session) = Primed();
        var later = new FeedReceipt(Receipt.ReceivedUtc.AddHours(1), Receipt.UpstreamUtc.AddHours(1));

        var result = Accept(store, session, Fetched(World(HomeWorld, 2, [Row(0, 0)]), later));

        Assert.True(result.Applied);
        Assert.Equal(0, result.MarkedNotReported);
        AssertBothStillListed(store);
    }

    [Fact]
    public void AnEmptyListingWithARowCountIsPartialNotEmpty()
    {
        var (store, session) = Primed();
        var later = new FeedReceipt(Receipt.ReceivedUtc.AddHours(1), Receipt.UpstreamUtc.AddHours(1));

        var result = Accept(store, session, Fetched(Encoding.UTF8.GetBytes("""{"id":63,"districts":[{"id":339,"num_open_plots":2,"open_plots":null}]}"""), later));

        Assert.True(result.Applied);
        AssertBothStillListed(store);
    }

    [Fact]
    public async Task AnOversizedBodyIsRefusedWhetherDeclaredOrStreamed()
    {
        var body = new byte[Limit + 1];

        var declared = await BoundedBody.ReadAsync(new MemoryStream(body), body.Length, Limit, CancellationToken.None);
        var streamed = await BoundedBody.ReadAsync(new MemoryStream(body), null, Limit, CancellationToken.None);
        var fitting = await BoundedBody.ReadAsync(new MemoryStream(body, 0, (int)Limit), null, Limit, CancellationToken.None);

        Assert.Null(declared);
        Assert.Null(streamed);
        Assert.Equal(Limit, fitting!.Length);
    }

    [Fact]
    public void ALateResponseForAWorldThePlayerLeftIsIgnored()
    {
        var store = new PlotStore();
        var session = new FeedSession();
        var left = session.Issue(OtherWorld);
        var current = session.Issue(HomeWorld);

        var late = FeedIntake.Accept(store, session, left, [HomeWorld], Fetched(World(OtherWorld, Row(0, 0, world: OtherWorld))), LotterySchedules.Create([]));
        var fresh = FeedIntake.Accept(store, session, current, [HomeWorld], Fetched(World(HomeWorld, Row(0, 0))), LotterySchedules.Create([]));

        Assert.Equal(FeedIntakeStatus.Stale, late.Status);
        Assert.True(fresh.Applied);
        Assert.DoesNotContain(Plot(1, 1, OtherWorld), store.Plots.Keys);
    }

    [Fact]
    public void AnOlderRequestForTheSameWorldCannotOverwriteANewerOne()
    {
        var store = new PlotStore();
        var session = new FeedSession();
        var older = session.Issue(HomeWorld);
        var newer = session.Issue(HomeWorld);

        var fresh = FeedIntake.Accept(store, session, newer, [HomeWorld], Fetched(World(HomeWorld, Row(0, 0))), LotterySchedules.Create([]));
        var late = FeedIntake.Accept(store, session, older, [HomeWorld], Fetched(World(HomeWorld)), LotterySchedules.Create([]));

        Assert.True(fresh.Applied);
        Assert.Equal(FeedIntakeStatus.Stale, late.Status);
        Assert.Equal(PlotAvailability.LotteryEntry, store.Plots[kept].AvailabilityAt(VerifiedEntryPhase()));
    }

    [Fact]
    public void ABodyForAnotherWorldThanRequestedIsRefused()
    {
        var store = new PlotStore();
        var session = new FeedSession();
        var ticket = session.Issue(HomeWorld);

        var result = FeedIntake.Accept(store, session, ticket, [HomeWorld], Fetched(World(OtherWorld, Row(0, 0, world: OtherWorld))), LotterySchedules.Create([]));

        Assert.Equal(FeedIntakeStatus.WrongWorld, result.Status);
        Assert.Empty(store.Plots);
    }

    [Fact]
    public void OnlyTheSelectedWorldAndWatchedWorldsAreFetched()
    {
        var worlds = FetchScope.Worlds(HomeWorld, [OtherWorld, HomeWorld, 0, OtherWorld]);

        Assert.Equal<uint>([HomeWorld, OtherWorld], worlds);
    }

    private static (PlotStore Store, FeedSession Session) Primed()
    {
        var store = new PlotStore();
        var session = new FeedSession();
        var result = Accept(store, session, Fetched(World(HomeWorld, Row(0, 0), Row(0, 1))));
        Assert.Equal(2, result.Merged);
        return (store, session);
    }

    private static FeedIntakeResult Accept(PlotStore store, FeedSession session, in FeedFetch fetch)
    {
        var ticket = session.Issue(HomeWorld);
        return FeedIntake.Accept(store, session, ticket, [HomeWorld], fetch, LotterySchedules.Create([]));
    }

    private static void AssertBothStillListed(PlotStore store)
    {
        Assert.Equal(PlotAvailability.LotteryEntry, store.Plots[kept].AvailabilityAt(VerifiedEntryPhase()));
        Assert.Equal(PlotAvailability.LotteryEntry, store.Plots[dropped].AvailabilityAt(VerifiedEntryPhase()));
    }
}
