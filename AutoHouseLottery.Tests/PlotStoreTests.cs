using AutoHouseLottery.Core.Housing.Model;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests;

public sealed class PlotStoreTests
{
    private static readonly PlotKey listed = Plot(1, 1);
    private static readonly PlotKey dropped = Plot(1, 2);

    [Fact]
    public void FailedResponseRemovesNothing()
    {
        var store = StoreWithTwoListedPlots();

        var result = store.Apply(Coverage(CoverageOutcome.Failed, ObservedAt.AddHours(1)), []);

        Assert.True(result.Accepted);
        Assert.Equal(0, result.MarkedNotReported);
        Assert.Equal(PlotAvailability.LotteryEntry, store.Plots[dropped].AvailabilityAt(VerifiedEntryPhase()));
        Assert.True(store.TryGetCoverage(Scope(), ObservationSource.PublicFeed, out var kept));
        Assert.Equal(ObservedAt, kept.ObservedFromUtc);
    }

    [Fact]
    public void MissingRowInACompleteListingIsNotReportedRatherThanOccupied()
    {
        var store = StoreWithTwoListedPlots();
        var later = ObservedAt.AddHours(1);

        var result = store.Apply(Coverage(CoverageOutcome.Complete, later), [Listing(listed, later)]);

        Assert.Equal(1, result.MarkedNotReported);
        Assert.Equal(PlotAvailability.NotReported, store.Plots[dropped].AvailabilityAt(VerifiedEntryPhase()));
        Assert.Equal(PlotAvailability.LotteryEntry, store.Plots[listed].AvailabilityAt(VerifiedEntryPhase()));
    }

    [Fact]
    public void PartialResponseKeepsUnlistedPlotsAndTheCompleteCoverage()
    {
        var store = StoreWithTwoListedPlots();
        var later = ObservedAt.AddHours(1);

        store.Apply(Coverage(CoverageOutcome.Partial, later), [Listing(listed, later)]);

        Assert.Equal(PlotAvailability.LotteryEntry, store.Plots[dropped].AvailabilityAt(VerifiedEntryPhase()));
        Assert.True(store.TryGetCoverage(Scope(), ObservationSource.PublicFeed, out var kept));
        Assert.Equal(CoverageOutcome.Complete, kept.Outcome);
    }

    [Fact]
    public void LateResponseForAnotherWorldIsRejectedWhole()
    {
        var store = StoreWithTwoListedPlots();
        var later = ObservedAt.AddHours(1);

        var result = store.Apply(Coverage(CoverageOutcome.Complete, later), [Listing(Plot(1, 1, world: OtherWorld), later)]);

        Assert.Equal(MergeRejection.IdentityMismatch, result.BatchRejection);
        Assert.Equal(2, store.Plots.Count);
        Assert.Equal(PlotAvailability.LotteryEntry, store.Plots[dropped].AvailabilityAt(VerifiedEntryPhase()));
    }

    [Fact]
    public void NewerPlacardOutlivesAnOlderListingOmission()
    {
        var store = StoreWithTwoListedPlots();
        var placardTime = ObservedAt.AddHours(2);
        store.Merge(new PlotObservation(dropped, Seen(ObservationSource.Placard, placardTime)) with { Sale = new SaleState(PlotSale.LotteryEntry, null) });

        store.Apply(Coverage(CoverageOutcome.Complete, ObservedAt.AddHours(1)), [Listing(listed, ObservedAt.AddHours(1))]);

        Assert.Equal(PlotAvailability.LotteryEntry, store.Plots[dropped].AvailabilityAt(VerifiedEntryPhase()));
    }

    [Fact]
    public void LaterResponseThatRelistsAnOmittedPlotRestoresIt()
    {
        var store = StoreWithTwoListedPlots();
        var omittedAt = ObservedAt.AddHours(1);
        store.Apply(Coverage(CoverageOutcome.Complete, omittedAt), [Listing(listed, omittedAt)]);
        Assert.Equal(PlotAvailability.NotReported, store.Plots[dropped].AvailabilityAt(VerifiedEntryPhase()));

        var relistedAt = ObservedAt.AddHours(2);
        store.Apply(Coverage(CoverageOutcome.Complete, relistedAt), [Listing(listed, relistedAt), Listing(dropped, ObservedAt.AddMinutes(30))]);

        Assert.Equal(PlotAvailability.LotteryEntry, store.Plots[dropped].AvailabilityAt(VerifiedEntryPhase()));
    }

    [Fact]
    public void ModestClockSkewIsAcceptedAndLargeSkewDowngradesTheCoverage()
    {
        var store = StoreWithTwoListedPlots();
        var received = ObservedAt.AddHours(1);
        var slowClock = new CoverageSnapshot(Scope(), ObservationSource.PublicFeed, CoverageOutcome.Complete, null, 2, received, received, received);

        var modest = store.Apply(slowClock, [SkewedListing(listed, received, TimeSpan.FromMinutes(30)), SkewedListing(dropped, received, TimeSpan.FromMinutes(30))]);
        var large = store.Apply(slowClock with { ObservedFromUtc = received.AddHours(2) }, [SkewedListing(listed, received, TimeSpan.FromHours(2))]);

        Assert.Equal(2, modest.Merged);
        Assert.Equal(CoverageOutcome.Complete, modest.RecordedOutcome);
        Assert.Equal(1, large.Rejected);
        Assert.Equal(MergeRejection.FutureTimestamp, large.FirstRowRejection);
        Assert.Equal(CoverageOutcome.Partial, large.RecordedOutcome);
        Assert.Equal(0, large.MarkedNotReported);
        Assert.Equal(PlotAvailability.LotteryEntry, store.Plots[dropped].AvailabilityAt(VerifiedEntryPhase()));
    }

    [Fact]
    public void ReadersKeepTheSnapshotTheyAlreadyHold()
    {
        var store = StoreWithTwoListedPlots();
        var before = store.Plots;
        var revision = store.Revision;

        store.Merge(new PlotObservation(listed, Seen(ObservationSource.Placard, ObservedAt.AddHours(3))) { Occupancy = PlotOccupancy.Occupied });

        Assert.Equal(PlotAvailability.LotteryEntry, before[listed].AvailabilityAt(VerifiedEntryPhase()));
        Assert.Equal(PlotAvailability.Occupied, store.Plots[listed].AvailabilityAt(VerifiedEntryPhase()));
        Assert.Equal(revision + 1, store.Revision);
    }

    private static PlotObservation SkewedListing(PlotKey key, DateTime receivedUtc, TimeSpan ahead)
    {
        return Listing(key, receivedUtc) with { Provenance = new Provenance(ObservationSource.PublicFeed, receivedUtc + ahead, receivedUtc, FirstCycle) };
    }

    private static PlotStore StoreWithTwoListedPlots()
    {
        var store = new PlotStore();
        var result = store.Apply(Coverage(CoverageOutcome.Complete, ObservedAt), [Listing(listed, ObservedAt), Listing(dropped, ObservedAt)]);
        Assert.Equal(2, result.Merged);
        return store;
    }

    private static PlotObservation Listing(PlotKey key, DateTime sourceTimeUtc)
    {
        return new PlotObservation(key, Seen(ObservationSource.PublicFeed, sourceTimeUtc))
        {
            Occupancy = PlotOccupancy.Vacant,
            Sale = new SaleState(PlotSale.LotteryEntry, CycleStart + LotteryCalendar.EntryPeriod),
            Price = Deposit,
            EntryCount = 2,
        };
    }

    private static CoverageScope Scope()
    {
        Assert.True(CoverageScope.TryForDistrict(GameRegion.Global, HomeWorld, HousingDistricts.Mist, out var scope));
        return scope;
    }

    private static CoverageSnapshot Coverage(CoverageOutcome outcome, DateTime observedUtc)
    {
        return new CoverageSnapshot(Scope(), ObservationSource.PublicFeed, outcome, null, 0, observedUtc, observedUtc, observedUtc);
    }
}
