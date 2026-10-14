using AutoHouseLottery.Core.Housing.Model;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests;

public sealed class PlotMergeTests
{
    private static readonly PlotKey target = Plot(12, 5);

    [Fact]
    public void NewerOwnedObservationReplacesAnOlderLocalVacancy()
    {
        var snapshot = Merge(
            null,
            Observation(ObservationSource.Placard, ObservedAt) with { Occupancy = PlotOccupancy.Vacant, Sale = new SaleState(PlotSale.LotteryEntry, null) },
            Observation(ObservationSource.WardScan, ObservedAt.AddHours(2)) with { Occupancy = PlotOccupancy.Occupied });

        Assert.Equal(PlotAvailability.Occupied, snapshot.AvailabilityAt(VerifiedEntryPhase()));
    }

    [Fact]
    public void OlderOwnedObservationDoesNotOverrideANewerVacancy()
    {
        var snapshot = Merge(
            null,
            Observation(ObservationSource.Placard, ObservedAt.AddHours(2)) with { Occupancy = PlotOccupancy.Vacant, Sale = new SaleState(PlotSale.LotteryEntry, null) },
            Observation(ObservationSource.WardScan, ObservedAt) with { Occupancy = PlotOccupancy.Occupied });

        Assert.Equal(PlotAvailability.LotteryEntry, snapshot.AvailabilityAt(VerifiedEntryPhase()));
        Assert.Equal(ObservationSource.Placard, snapshot.Occupancy!.Value.Provenance.Source);
    }

    [Fact]
    public void WardPriceKeepsTheOlderEntryCountAndItsTimestamp()
    {
        var snapshot = Merge(
            null,
            Observation(ObservationSource.PublicFeed, ObservedAt) with { Price = Deposit, EntryCount = 4 },
            Observation(ObservationSource.WardScan, ObservedAt.AddHours(3)) with { Price = 2_900_000 });

        Assert.Equal(2_900_000, snapshot.Price!.Value.Value);
        Assert.Equal(ObservedAt.AddHours(3), snapshot.Price.Value.Provenance.SourceTimeUtc);
        Assert.Equal(4, snapshot.EntryCountIn(FirstCycle));
        Assert.Equal(ObservedAt, snapshot.EntryCount!.Value.Provenance.SourceTimeUtc);
        Assert.Equal(ObservationSource.PublicFeed, snapshot.EntryCount.Value.Provenance.Source);
    }

    [Fact]
    public void DownloadingTheSameSourceAgainDoesNotMakeTheCountFresh()
    {
        var refetched = Observation(ObservationSource.PublicFeed, ObservedAt) with
        {
            Provenance = new Provenance(ObservationSource.PublicFeed, ObservedAt, ObservedAt.AddHours(5), FirstCycle),
            EntryCount = 4,
        };
        var snapshot = Merge(null, Observation(ObservationSource.PublicFeed, ObservedAt) with { EntryCount = 4 }, refetched);

        Assert.Equal(ObservedAt, snapshot.EntryCount!.Value.Provenance.SourceTimeUtc);
    }

    [Fact]
    public void NewCycleDropsThePreviousCycleCount()
    {
        var nextCycleTime = CycleStart.AddDays(9).AddHours(1);
        var snapshot = Merge(
            null,
            Observation(ObservationSource.PublicFeed, ObservedAt) with { EntryCount = 5 },
            Observation(ObservationSource.PublicFeed, nextCycleTime, SecondCycle) with { Sale = new SaleState(PlotSale.LotteryEntry, null) });

        Assert.Null(snapshot.EntryCount);
        Assert.Null(snapshot.EntryCountIn(SecondCycle));
    }

    [Fact]
    public void PreviousCycleCountIsNeverCurrent()
    {
        var snapshot = Merge(null, Observation(ObservationSource.PublicFeed, ObservedAt) with { EntryCount = 5 });

        Assert.Equal(5, snapshot.EntryCountIn(FirstCycle));
        Assert.Null(snapshot.EntryCountIn(SecondCycle));
    }

    [Fact]
    public void LateCountFromAnEarlierCycleDoesNotReplaceTheCurrentOne()
    {
        var snapshot = Merge(
            null,
            Observation(ObservationSource.PublicFeed, CycleStart.AddDays(10), SecondCycle) with { EntryCount = 1 },
            Observation(ObservationSource.Placard, CycleStart.AddDays(11), FirstCycle) with { EntryCount = 9 });

        Assert.Equal(1, snapshot.EntryCountIn(SecondCycle));
    }

    [Fact]
    public void EntryPhaseObservationIsNotCurrentInResultsOrTheNextCycle()
    {
        var snapshot = Merge(null, Observation(ObservationSource.PublicFeed, ObservedAt) with { Sale = new SaleState(PlotSale.LotteryEntry, null) });
        var results = LotteryCalendar.Estimate(LotteryCalendar.Global, CycleStart.AddDays(6));
        var nextEntry = LotteryCalendar.Estimate(LotteryCalendar.Global, CycleStart.AddDays(10));

        Assert.Equal(PlotAvailability.LotteryEntry, snapshot.AvailabilityAt(VerifiedEntryPhase()));
        Assert.Equal(PlotAvailability.Unknown, snapshot.AvailabilityAt(results));
        Assert.Equal(PlotAvailability.Unknown, snapshot.AvailabilityAt(nextEntry));
    }

    [Fact]
    public void MissingCountStaysUnknownAndZeroIsAKnownCount()
    {
        var unknown = Merge(null, Observation(ObservationSource.PublicFeed, ObservedAt) with { Sale = new SaleState(PlotSale.LotteryEntry, null) });
        var zero = Merge(null, Observation(ObservationSource.PublicFeed, ObservedAt) with { EntryCount = 0 });

        Assert.Null(unknown.EntryCountIn(FirstCycle));
        Assert.Equal(0, zero.EntryCountIn(FirstCycle));
    }

    [Fact]
    public void InvalidObservationsAreRejectedAndChangeNothing()
    {
        var existing = Merge(null, Observation(ObservationSource.PublicFeed, ObservedAt) with { EntryCount = 3, Price = Deposit });
        var observation = Observation(ObservationSource.PublicFeed, ObservedAt.AddHours(1));

        AssertRejected(existing, observation with { EntryCount = -1 }, MergeRejection.NegativeEntryCount);
        AssertRejected(existing, observation with { Price = 0 }, MergeRejection.InvalidPrice);
        AssertRejected(existing, observation with { Size = PlotSize.Unknown }, MergeRejection.UnknownValue);
        AssertRejected(existing, observation with { Key = default }, MergeRejection.InvalidAddress);
        AssertRejected(existing, observation with { Key = Plot(12, 6) }, MergeRejection.IdentityMismatch);
        AssertRejected(
            existing,
            observation with { Provenance = new Provenance(ObservationSource.PublicFeed, ObservedAt.AddDays(1), ObservedAt, FirstCycle) },
            MergeRejection.FutureTimestamp);
        AssertRejected(
            existing,
            observation with { Provenance = new Provenance(ObservationSource.PublicFeed, default, ObservedAt, FirstCycle) },
            MergeRejection.InvalidTimestamp);
        AssertRejected(
            existing,
            observation with { Provenance = new Provenance(ObservationSource.PublicFeed, ObservedAt, ObservedAt, null), EntryCount = 2 },
            MergeRejection.MissingCycle);
        AssertRejected(
            existing,
            observation with { Provenance = Seen(ObservationSource.PublicFeed, ObservedAt, new CycleKey(GameRegion.China, 1)) },
            MergeRejection.IdentityMismatch);
    }

    [Fact]
    public void SimultaneousDisagreementBetweenSourcesNeedsVerification()
    {
        var snapshot = Merge(
            null,
            Observation(ObservationSource.WardScan, ObservedAt) with { Size = PlotSize.Small },
            Observation(ObservationSource.PublicFeed, ObservedAt.AddMinutes(1)) with { Size = PlotSize.Medium });

        Assert.True(snapshot.NeedsVerification);
        Assert.Equal(PlotSize.Small, snapshot.Size!.Value.Value);
        var conflict = Assert.Single(snapshot.Conflicts);
        Assert.Equal(PlotField.Size, conflict.Field);
        Assert.Equal(ObservationSource.PublicFeed, conflict.Other.Source);
    }

    [Fact]
    public void OccupiedAgainstAnOpenListingNeedsVerificationUntilThePlacardSettlesIt()
    {
        var contradicted = Merge(
            null,
            Observation(ObservationSource.WardScan, ObservedAt) with { Occupancy = PlotOccupancy.Occupied },
            Observation(ObservationSource.PublicFeed, ObservedAt.AddMinutes(1)) with { Sale = new SaleState(PlotSale.LotteryEntry, null) });

        Assert.True(contradicted.NeedsVerification);
        Assert.Equal(PlotField.Availability, Assert.Single(contradicted.Conflicts).Field);
        Assert.Equal(PlotAvailability.Unknown, contradicted.AvailabilityAt(VerifiedEntryPhase()));

        var settled = Merge(
            contradicted,
            Observation(ObservationSource.Placard, ObservedAt.AddMinutes(2)) with { Occupancy = PlotOccupancy.Vacant, Sale = new SaleState(PlotSale.LotteryEntry, null) });

        Assert.False(settled.NeedsVerification);
        Assert.Equal(PlotAvailability.LotteryEntry, settled.AvailabilityAt(VerifiedEntryPhase()));
    }

    [Fact]
    public void AnUpdateFromTheSameSourceIsNotAContradiction()
    {
        var snapshot = Merge(
            null,
            Observation(ObservationSource.PublicFeed, ObservedAt) with { Size = PlotSize.Small },
            Observation(ObservationSource.PublicFeed, ObservedAt.AddMinutes(1)) with { Size = PlotSize.Medium });

        Assert.False(snapshot.NeedsVerification);
        Assert.Equal(PlotSize.Medium, snapshot.Size!.Value.Value);
    }

    [Fact]
    public void SingleObservationThatIsOccupiedAndOfferedIsRejected()
    {
        var observation = Observation(ObservationSource.PublicFeed, ObservedAt) with
        {
            Occupancy = PlotOccupancy.Occupied,
            Sale = new SaleState(PlotSale.LotteryEntry, null),
        };

        Assert.Equal(MergeRejection.ContradictoryFields, PlotMerge.Validate(observation));
    }

    [Fact]
    public void RepeatedContradictingRefreshesKeepOneConflict()
    {
        var snapshot = Merge(null, Observation(ObservationSource.WardScan, ObservedAt) with { Size = PlotSize.Small });
        for (var refresh = 1; refresh <= 5; refresh++)
        {
            var again = Observation(ObservationSource.PublicFeed, ObservedAt.AddMinutes(1)) with
            {
                Provenance = new Provenance(ObservationSource.PublicFeed, ObservedAt.AddMinutes(1), ObservedAt.AddMinutes(refresh * 20), FirstCycle),
                Size = PlotSize.Medium,
            };
            snapshot = Merge(snapshot, again);
        }

        Assert.Single(snapshot.Conflicts);
    }

    private static PlotObservation Observation(ObservationSource source, DateTime sourceTimeUtc, CycleKey? cycle = null)
    {
        return new PlotObservation(target, Seen(source, sourceTimeUtc, cycle));
    }

    private static PlotSnapshot Merge(PlotSnapshot? start, params PlotObservation[] observations)
    {
        var snapshot = start;
        for (var index = 0; index < observations.Length; index++)
        {
            Assert.Equal(MergeRejection.None, PlotMerge.TryMerge(snapshot, observations[index], out var merged));
            snapshot = merged;
        }

        return snapshot!;
    }

    private static void AssertRejected(PlotSnapshot existing, PlotObservation observation, MergeRejection expected)
    {
        Assert.Equal(expected, PlotMerge.TryMerge(existing, observation, out var unchanged));
        Assert.Same(existing, unchanged);
    }
}
