using System.Collections.Immutable;
using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Selection.Model;
using static AutoHouseLottery.Tests.CharacterFixtures;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests;

public sealed class AssignmentPlannerTests
{
    private const long Reserve = 100_000;
    private const long Travel = 10_000;

    private static readonly DateTime now = CycleStart.AddHours(12);

    [Fact]
    public void FortyTwoPlotShortlistWithOneCharacterMakesOneEntry()
    {
        var keys = Ward(1, 42);
        var plots = Listed(keys, entries: plotIndex => plotIndex % 7);

        var preview = Plan(Policy(keys, Settings(1)), [Character(1)], plots);

        Assert.True(preview.CanRun);
        Assert.Equal(42, preview.PlotCount);
        Assert.Equal(1, preview.MaxNewEntries);
        var assignment = Assert.Single(preview.Assignments);
        Assert.Equal(RunPolicy.MaximumInspections, assignment.TargetOrder.Length);
        Assert.Equal(preview.Ranking[0].Key, assignment.TargetOrder[0]);
        Assert.Equal(Deposit, preview.TotalMaximumDeposit);
    }

    [Fact]
    public void AnEstimatedCycleCannotAuthorizeTheRun()
    {
        var keys = Ward(1, 3);
        var policy = Policy(keys, Settings(1)) with { Cycle = LotteryCalendar.Estimate(LotteryCalendar.Global, CycleStart.AddHours(1)) };

        var preview = Plan(policy, [Character(1)], Listed(keys));

        Assert.False(preview.CanRun);
        Assert.Equal(RunBlocker.CycleNotVerified, preview.Blockers);
        Assert.Single(preview.Assignments);
    }

    [Fact]
    public void InspectionLimitAboveFiveIsAnInvalidPolicy()
    {
        var keys = Ward(1, 3);

        var preview = Plan(Policy(keys, Settings(1)) with { MaxInspections = 6 }, [Character(1)], Listed(keys));

        Assert.Equal(RunPolicyError.InspectionLimitOutOfRange, preview.PolicyError);
        Assert.Equal(RunBlocker.InvalidPolicy, preview.Blockers);
        Assert.Empty(preview.Assignments);
    }

    [Fact]
    public void ExactTargetNeverGainsAFallback()
    {
        var keys = Ward(1, 3);
        var plots = Listed(keys);
        var policy = Policy(keys, Settings(1)) with { Shortlist = null, Exact = new ExactTarget(keys[2], Deposit) };

        var preview = Plan(policy, [Character(1)], plots);

        Assert.Equal<PlotKey>([keys[2]], Assert.Single(preview.Assignments).TargetOrder);
    }

    [Fact]
    public void UnavailableExactTargetIsNotReplacedSilently()
    {
        var keys = Ward(1, 3);
        var plots = Listed(keys);
        plots[keys[0]] = plots[keys[0]] with
        {
            Occupancy = new Observed<PlotOccupancy>(PlotOccupancy.Occupied, Seen(ObservationSource.WardScan, ObservedAt.AddHours(1))),
        };
        var policy = Policy(keys, Settings(1)) with { Shortlist = null, Exact = new ExactTarget(keys[0], Deposit) };

        var preview = Plan(policy, [Character(1)], plots);

        Assert.Empty(preview.Assignments);
        Assert.Equal(EligibilityReason.PlotUnavailable, Assert.Single(preview.Exclusions).Reasons);
        Assert.True(preview.Blockers.HasFlag(RunBlocker.NoAssignment));
    }

    [Fact]
    public void PlotsOnAnotherWorldAreOnlyForCharactersFromThatWorld()
    {
        PlotKey[] keys = [Plot(1, 1, world: OtherWorld), Plot(1, 2)];

        var preview = Plan(Policy(keys, Settings(1)), [Character(1)], Listed(keys));

        Assert.Equal<PlotKey>([Plot(1, 2)], Assert.Single(preview.Assignments).TargetOrder);
    }

    [Fact]
    public void UngroupedPrivateCharactersOnOneWorldShareTheTopPlot()
    {
        var keys = Ward(1, 4);

        var preview = Plan(Policy(keys, Settings(1), Settings(2)), [Character(1), Character(2)], Listed(keys));

        Assert.Equal(2, preview.MaxNewEntries);
        Assert.Equal(PlacementMode.SamePlot, preview.Assignments[0].Placement);
        Assert.Equal<PlotKey>(preview.Assignments[0].TargetOrder, preview.Assignments[1].TargetOrder);
        Assert.Empty(preview.GroupOdds);
        Assert.True(preview.CanRun);
    }

    [Fact]
    public void SeparateServiceAccountsSpreadAcrossPlotsInRankOrder()
    {
        var keys = Ward(1, 4);

        var preview = Plan(
            Policy(keys, Settings(1), Settings(2)),
            [Character(1, accountGroup: 1), Character(2, accountGroup: 2)],
            Listed(keys));

        Assert.Equal(preview.Ranking[0].Key, preview.Assignments[0].TargetOrder[0]);
        Assert.Equal(preview.Ranking[1].Key, preview.Assignments[1].TargetOrder[0]);
        Assert.True(preview.CanRun);
    }

    [Fact]
    public void SamePlotGroupFollowsThePlotOfTheFirstConfirmedEntry()
    {
        var keys = Ward(1, 4);
        var preview = Plan(Policy(keys, Settings(1), Settings(2)), [Character(1), Character(2)], Listed(keys));
        var fallbackPlot = preview.Assignments[0].TargetOrder[1];

        var followed = AssignmentPlanner.FollowConfirmedEntry(preview.Assignments, 1, fallbackPlot);

        Assert.Equal<PlotKey>([fallbackPlot], followed[1].TargetOrder);
        Assert.Equal<PlotKey>(preview.Assignments[0].TargetOrder, followed[0].TargetOrder);
    }

    [Fact]
    public void SpreadInsideOneGroupNeedsExplicitConfirmation()
    {
        var keys = Ward(1, 4);
        var roster = new[] { Character(1), Character(2), Character(3) };
        var spread = Policy(keys, Settings(1), Settings(2), Settings(3)) with { Placement = PlacementChoice.Spread };

        var unconfirmed = Plan(spread, roster, Listed(keys, entries: _ => 2));
        var confirmed = Plan(spread with { MultipleWinRiskAccepted = true }, roster, Listed(keys, entries: _ => 2));

        Assert.Equal(RunBlocker.MultipleWinsNeedConfirmation, unconfirmed.Blockers);
        var groupOdds = Assert.Single(unconfirmed.GroupOdds).Estimate!.Value;
        Assert.Equal(26, (int)Math.Round(groupOdds.TwoOrMoreWins * 100));
        Assert.Equal(70, (int)Math.Round(unconfirmed.Odds!.Value.AnyWin * 100));
        Assert.True(confirmed.CanRun);
    }

    [Fact]
    public void UnknownCountHidesTheOddsEstimate()
    {
        var keys = Ward(1, 2);
        var policy = Policy(keys, Settings(1), Settings(2)) with { Placement = PlacementChoice.Spread, MultipleWinRiskAccepted = true };

        var preview = Plan(policy, [Character(1), Character(2)], Listed(keys, entries: _ => null));

        Assert.Null(preview.Odds);
        Assert.Null(Assert.Single(preview.GroupOdds).Estimate);
    }

    [Fact]
    public void PrivateEntryLeavesNoSlotForAFreeCompanyEntry()
    {
        var keys = Ward(1, 2);
        var entered = Character(1) with { Participation = new CycleParticipation(FirstCycle, EntrySlotState.Entered) };

        var preview = Plan(Policy(keys, Settings(1, BuyerType.FreeCompany)), [entered], Listed(keys));

        Assert.Equal(0, preview.MaxNewEntries);
        Assert.Equal(EligibilityReason.AlreadyEnteredThisCycle, Assert.Single(preview.Exclusions).Reasons);
    }

    [Fact]
    public void LandOwnerOnTheAccountIsExcludedWithItsReason()
    {
        var keys = Ward(1, 2);

        var preview = Plan(Policy(keys, Settings(1)), [Character(1) with { AccountOwnsLandOnHomeWorld = true }], Listed(keys));

        Assert.Equal(EligibilityReason.AccountOwnsLandOnWorld, Assert.Single(preview.Exclusions).Reasons);
    }

    [Fact]
    public void OutstandingRefundIsCollectedFirstOnlyWhenAutomaticRefundsAreOn()
    {
        var keys = Ward(1, 2);
        var refundDue = Character(1) with { Outstanding = OutstandingSettlement.RefundDue };

        var automatic = Plan(Policy(keys, Settings(1)), [refundDue], Listed(keys));
        var manual = Plan(Policy(keys, Settings(1)) with { CollectRefundsFirst = false }, [refundDue], Listed(keys));

        Assert.True(Assert.Single(automatic.Assignments).CollectsRefundFirst);
        Assert.Equal(EligibilityReason.OutstandingRefundOrProperty, Assert.Single(manual.Exclusions).Reasons);
    }

    [Fact]
    public void EachCharacterIsCheckedAgainstItsOwnGil()
    {
        var keys = Ward(1, 2);
        var exactlyEnough = Deposit + Travel + Reserve;
        var roster = new[]
        {
            Character(1, accountGroup: 1),
            Character(2, accountGroup: 2, gil: exactlyEnough),
            Character(3, accountGroup: 3, gil: exactlyEnough - 1),
        };

        var preview = Plan(Policy(keys, Settings(1), Settings(2), Settings(3)), roster, Listed(keys));

        Assert.Equal(2, preview.MaxNewEntries);
        var exclusion = Assert.Single(preview.Exclusions);
        Assert.Equal(3UL, exclusion.ContentId);
        Assert.Equal(EligibilityReason.InsufficientGil, exclusion.Reasons);
        Assert.Equal(2 * Deposit, preview.TotalMaximumDeposit);
    }

    [Fact]
    public void UnknownPriceReservesTheCharacterCap()
    {
        var keys = Ward(1, 1);
        var plots = Listed(keys);
        plots[keys[0]] = plots[keys[0]] with { Price = null };

        var preview = Plan(Policy(keys, Settings(1) with { DepositCap = 5_000_000 }), [Character(1)], plots);

        Assert.Equal(5_000_000, Assert.Single(preview.Assignments).MaximumDeposit);
        Assert.Equal(EligibilityVerdict.Unknown, preview.Assignments[0].Eligibility.Verdict);
    }

    [Fact]
    public void PositionFromAnEarlierEntryPeriodNoLongerAuthorizes()
    {
        var keys = Ward(1, 2);

        var preview = AssignmentPlanner.Build(Policy(keys, Settings(1)), [Character(1)], Listed(keys), CycleStart.AddDays(5));

        Assert.Equal(RunBlocker.CycleExpired, preview.Blockers);
    }

    [Fact]
    public void SelectedCharacterWithoutAProfileIsExcludedNotFatal()
    {
        var keys = Ward(1, 2);

        var preview = Plan(Policy(keys, Settings(1), Settings(2)), [Character(1)], Listed(keys));

        Assert.Equal(1, preview.MaxNewEntries);
        var exclusion = Assert.Single(preview.Exclusions);
        Assert.Equal(2UL, exclusion.ContentId);
        Assert.Equal(EligibilityReason.ProfileUnavailable, exclusion.Reasons);
    }

    [Fact]
    public void FreeCompanyMembersEnterTheSamePlotWithTheirOwnSlots()
    {
        var keys = Ward(1, 4);
        var policy = Policy(keys, Settings(1, BuyerType.FreeCompany), Settings(2, BuyerType.FreeCompany), Settings(3, BuyerType.FreeCompany));

        var preview = Plan(policy, [Character(1, accountGroup: 1), Character(2, accountGroup: 2), Character(3, accountGroup: 3)], Listed(keys));

        Assert.Equal(3, preview.MaxNewEntries);
        Assert.Equal(OwnershipGroupKind.FreeCompany, preview.Assignments[0].Group.Kind);
        for (var index = 0; index < preview.Assignments.Length; index++)
        {
            Assert.Equal(PlacementMode.SamePlot, preview.Assignments[index].Placement);
            Assert.Equal(preview.Ranking[0].Key, preview.Assignments[index].TargetOrder[0]);
        }

        Assert.Empty(preview.GroupOdds);
    }

    [Fact]
    public void OneUngroupedCharacterKeepsTheWholeWorldOnOnePlot()
    {
        var keys = Ward(1, 4);

        var preview = Plan(
            Policy(keys, Settings(1), Settings(2), Settings(3)),
            [Character(1, accountGroup: 1), Character(2, accountGroup: 2), Character(3)],
            Listed(keys));

        Assert.Equal(preview.Assignments[0].TargetOrder[0], preview.Assignments[1].TargetOrder[0]);
        Assert.Equal(preview.Assignments[0].TargetOrder[0], preview.Assignments[2].TargetOrder[0]);
        Assert.True(preview.CanRun);
    }

    [Fact]
    public void PlotThatNeedsVerificationIsLeftOutOfThePlan()
    {
        var keys = Ward(1, 3);
        var plots = Listed(keys, entries: _ => 0);
        plots[keys[0]] = plots[keys[0]] with
        {
            Conflicts =
            [
                new FieldConflict(
                    PlotField.Availability,
                    (byte)PlotOccupancy.Occupied,
                    Seen(ObservationSource.WardScan, ObservedAt),
                    (byte)PlotSale.LotteryEntry,
                    Seen(ObservationSource.PublicFeed, ObservedAt.AddMinutes(1))),
            ],
        };

        var bestMatch = Plan(Policy(keys, Settings(1)), [Character(1)], plots);
        var exact = Plan(Policy(keys, Settings(1)) with { Shortlist = null, Exact = new ExactTarget(keys[0], Deposit) }, [Character(1)], plots);

        Assert.DoesNotContain(keys[0], Assert.Single(bestMatch.Assignments).TargetOrder);
        Assert.Equal(PlotAvailability.Unknown, plots[keys[0]].AvailabilityAt(VerifiedEntryPhase()));
        Assert.True(Assert.Single(exact.Exclusions).Reasons.HasFlag(EligibilityReason.PlotNeedsVerification));
    }

    [Fact]
    public void FollowUpRejectsAPlotOutsideTheLeadsTargets()
    {
        var keys = Ward(1, 8);
        var preview = Plan(Policy(keys, Settings(1), Settings(2)), [Character(1), Character(2)], Listed(keys));

        Assert.Throws<ArgumentException>(() => AssignmentPlanner.FollowConfirmedEntry(preview.Assignments, 1, keys[7]));
    }

    [Fact]
    public void ProviderRefreshDuringPreviewChangesTheStoreRevisionButNotThePolicy()
    {
        var keys = Ward(1, 2);
        var store = new PlotStore();
        Assert.True(CoverageScope.TryForDistrict(GameRegion.Global, HomeWorld, HousingDistricts.Mist, out var scope));
        var firstFetch = ObservedAt;
        store.Apply(Coverage(scope, firstFetch), [Row(keys[0], firstFetch, 0), Row(keys[1], firstFetch, 5)]);
        var policy = Policy(keys, Settings(1));
        var revision = store.Revision;
        var preview = Plan(policy, [Character(1)], store.Plots);

        var refresh = ObservedAt.AddHours(1);
        store.Apply(Coverage(scope, refresh), [Row(keys[0], refresh, 9), Row(keys[1], refresh, 1)]);
        var refreshed = Plan(policy, [Character(1)], store.Plots);

        Assert.NotEqual(revision, store.Revision);
        Assert.Equal(keys[0], preview.Ranking[0].Key);
        Assert.Equal(0, preview.Ranking[0].ReportedEntries);
        Assert.Equal(keys[1], refreshed.Ranking[0].Key);
        Assert.Equal(1, policy.Revision);
        Assert.Equal<PlotKey>(keys, policy.Shortlist!.Value.Plots);
    }

    [Fact]
    public void OwnEntryAlreadyCountedOnThePlotIsNotTreatedAsAnotherPlayer()
    {
        var keys = Ward(1, 1);
        var plots = Listed(keys, entries: _ => 2);
        var entered = Character(9) with
        {
            Participation = new CycleParticipation(FirstCycle, EntrySlotState.Entered) { EnteredPlot = keys[0], EnteredUtc = ObservedAt.AddHours(-1) },
        };

        var preview = Plan(Policy(keys, Settings(1)), [Character(1), entered], plots);
        var unknownTime = Plan(
            Policy(keys, Settings(1)),
            [Character(1), entered with { Participation = entered.Participation with { EnteredUtc = null } }],
            plots);

        Assert.Equal(67, (int)Math.Round(preview.Odds!.Value.AnyWin * 100));
        Assert.Null(unknownTime.Odds);
    }

    private static CoverageSnapshot Coverage(CoverageScope scope, DateTime observedUtc)
    {
        return new CoverageSnapshot(scope, ObservationSource.PublicFeed, CoverageOutcome.Complete, null, 2, observedUtc, observedUtc, observedUtc);
    }

    private static PlotObservation Row(PlotKey key, DateTime observedUtc, int entries)
    {
        return new PlotObservation(key, Seen(ObservationSource.PublicFeed, observedUtc))
        {
            Occupancy = PlotOccupancy.Vacant,
            Sale = new SaleState(PlotSale.LotteryEntry, CycleStart + LotteryCalendar.EntryPeriod),
            Price = Deposit,
            Buyers = PlotBuyers.Both,
            EntryCount = entries,
        };
    }

    private static RunPreview Plan(RunPolicy policy, ReadOnlySpan<CharacterProfile> roster, IReadOnlyDictionary<PlotKey, PlotSnapshot> plots)
    {
        return AssignmentPlanner.Build(policy, roster, plots, now);
    }

    private static RunPolicy Policy(PlotKey[] keys, params CharacterRunSettings[] characters) => new()
    {
        Revision = 1,
        Cycle = VerifiedEntryPhase(),
        Characters = [.. characters],
        TravelAllowance = Travel,
        Shortlist = new ShortlistSnapshot(Guid.NewGuid(), 1, [.. keys]),
    };

    private static CharacterRunSettings Settings(ulong contentId, BuyerType buyer = BuyerType.Private) => new(contentId, buyer, Deposit, Reserve);

    private static PlotKey[] Ward(int ward, int plotCount)
    {
        var keys = new PlotKey[plotCount];
        for (var index = 0; index < plotCount; index++)
        {
            keys[index] = Plot(ward, index + 1);
        }

        return keys;
    }

    private static Dictionary<PlotKey, PlotSnapshot> Listed(PlotKey[] keys, Func<int, int?>? entries = null)
    {
        var plots = new Dictionary<PlotKey, PlotSnapshot>();
        for (var index = 0; index < keys.Length; index++)
        {
            plots[keys[index]] = ListedPlot(keys[index], entries is null ? index : entries(index));
        }

        return plots;
    }
}
