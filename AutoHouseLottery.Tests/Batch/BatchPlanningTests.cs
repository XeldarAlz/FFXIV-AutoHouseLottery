using System.Collections.Immutable;
using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Integration.Model;
using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Selection.Model;
using static AutoHouseLottery.Tests.CharacterFixtures;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests.Batch;

// VALIDATION_PLAN section 4 for several characters: exact targets, ownership groups, placement and funds.
public sealed class BatchPlanningTests
{
    private const long Reserve = 100_000;

    private static readonly DateTime now = CycleStart.AddHours(12);

    [Fact]
    public void AnExactTargetIsEnteredAloneWithNoFallback()
    {
        var keys = Ward(6);
        var preview = Plan(Policy(keys, Settings(1) with { Target = keys[3] }), [Character(1)], Listed(keys));

        var assignment = Assert.Single(preview.Assignments);
        Assert.True(assignment.Exact);
        Assert.Equal<PlotKey>([keys[3]], assignment.TargetOrder);
    }

    [Fact]
    public void ATargetOffTheShortlistIsAnInvalidPolicy()
    {
        var keys = Ward(3);

        var preview = Plan(Policy(keys, Settings(1) with { Target = Plot(9, 9) }), [Character(1)], Listed(keys));

        Assert.Equal(RunPolicyError.InvalidExactTarget, preview.PolicyError);
        Assert.Empty(preview.Assignments);
    }

    [Fact]
    public void ACharacterTargetBesideAnExactPolicyIsAmbiguous()
    {
        var keys = Ward(2);
        var policy = Policy(keys, Settings(1) with { Target = keys[0] }) with { Shortlist = null, Exact = new ExactTarget(keys[0], Deposit) };

        Assert.Equal(RunPolicyError.AmbiguousTarget, RunPolicyRules.Validate(policy));
    }

    [Fact]
    public void AGroupAnchoredOnAnExactTargetHasNoFallbackForAnyMember()
    {
        var keys = Ward(5);
        var roster = new[] { Character(1), Character(2), Character(3) };

        var preview = Plan(Policy(keys, Settings(1), Settings(2) with { Target = keys[4] }, Settings(3)), roster, Listed(keys));

        Assert.Equal(3, preview.MaxNewEntries);
        for (var index = 0; index < preview.Assignments.Length; index++)
        {
            Assert.Equal<PlotKey>([keys[4]], preview.Assignments[index].TargetOrder);
        }

        Assert.True(preview.Assignments[0].Exact);
        Assert.Equal(2UL, preview.Assignments[0].ContentId);
        Assert.Empty(preview.GroupOdds);
        Assert.True(preview.CanRun);
    }

    [Fact]
    public void AMemberThatCannotEnterTheAnchoredPlotIsLeftOutRatherThanEnteringAnother()
    {
        var keys = Ward(5);
        var plots = Listed(keys);
        plots[keys[4]] = ListedPlot(keys[4], 4, price: 2 * Deposit);
        var policy = Policy(keys, Settings(1) with { Target = keys[4], DepositCap = 2 * Deposit }, Settings(2));

        var preview = Plan(policy, [Character(1), Character(2)], plots);

        Assert.Equal<PlotKey>([keys[4]], Assert.Single(preview.Assignments).TargetOrder);
        var left = Assert.Single(preview.Exclusions);
        Assert.Equal(2UL, left.ContentId);
        Assert.Equal(EligibilityReason.GroupOnAnotherPlot | EligibilityReason.DepositOverCap, left.Reasons);
    }

    [Fact]
    public void ASecondExactTargetOnAnotherPlotInOneGroupIsLeftOut()
    {
        var keys = Ward(5);

        var preview = Plan(Policy(keys, Settings(1) with { Target = keys[4] }, Settings(2) with { Target = keys[1] }), [Character(1), Character(2)], Listed(keys));

        Assert.Equal<PlotKey>([keys[4]], Assert.Single(preview.Assignments).TargetOrder);
        Assert.Equal(new CharacterExclusion(2, EligibilityReason.GroupOnAnotherPlot), Assert.Single(preview.Exclusions));
        Assert.Empty(preview.GroupOdds);
    }

    [Fact]
    public void AMemberAlreadyEnteredThisCycleAnchorsItsGroupOnThatPlot()
    {
        var keys = Ward(5);
        var entered = Character(3) with
        {
            Participation = new CycleParticipation(FirstCycle, EntrySlotState.Entered) { EnteredPlot = keys[2], EnteredUtc = ObservedAt },
        };
        var elsewhere = entered with { Participation = entered.Participation with { EnteredPlot = Plot(9, 9) } };

        var onShortlist = Plan(Policy(keys, Settings(1), Settings(2), Settings(3)), [Character(1), Character(2), entered], Listed(keys));
        var offShortlist = Plan(Policy(keys, Settings(1), Settings(2), Settings(3)), [Character(1), Character(2), elsewhere], Listed(keys));

        Assert.Equal(2, onShortlist.MaxNewEntries);
        for (var index = 0; index < onShortlist.Assignments.Length; index++)
        {
            Assert.Equal<PlotKey>([keys[2]], onShortlist.Assignments[index].TargetOrder);
        }

        Assert.Empty(offShortlist.Assignments);
        Assert.Equal(2, CountReason(offShortlist, EligibilityReason.GroupOnAnotherPlot));
    }

    [Fact]
    public void AMemberWithAnEntryInQuestionAnchorsItsGroupOnThatPlot()
    {
        var keys = Ward(5);
        var inQuestion = Character(3) with
        {
            Participation = new CycleParticipation(FirstCycle, EntrySlotState.Unresolved) { EnteredPlot = keys[2], ObservedUtc = ObservedAt },
        };

        var plan = Plan(Policy(keys, Settings(1), Settings(2), Settings(3)), [Character(1), Character(2), inQuestion], Listed(keys));

        Assert.Equal(2, plan.Assignments.Length);
        for (var index = 0; index < plan.Assignments.Length; index++)
        {
            Assert.Equal<PlotKey>([keys[2]], plan.Assignments[index].TargetOrder);
        }
    }

    [Fact]
    public void AnEntryOnAPlotThatCouldNotBeReadLeavesItsGroupOut()
    {
        var keys = Ward(5);
        var unread = Character(3) with { Participation = new CycleParticipation(FirstCycle, EntrySlotState.Entered) { EnteredUtc = ObservedAt } };

        var plan = Plan(Policy(keys, Settings(1), Settings(2), Settings(3)), [Character(1), Character(2), unread], Listed(keys));

        Assert.Empty(plan.Assignments);
        Assert.Equal(2, CountReason(plan, EligibilityReason.GroupOnAnotherPlot));
    }

    [Fact]
    public void EachCharactersOwnRefundSettingDecidesWhetherAnEarlierRefundRulesItOut()
    {
        var keys = Ward(2);
        var refundDue = Character(1) with { Outstanding = OutstandingSettlement.RefundDue };

        var automatic = Plan(Policy(keys, Settings(1)), [refundDue], Listed(keys));
        var manual = Plan(Policy(keys, Settings(1) with { ManualRefunds = true }), [refundDue], Listed(keys));

        Assert.True(Assert.Single(automatic.Assignments).CollectsRefundFirst);
        Assert.Equal(EligibilityReason.OutstandingRefundOrProperty, Assert.Single(manual.Exclusions).Reasons);
    }

    [Fact]
    public void SamePlotFollowersTakeThePlotTheFirstMemberFellBackTo()
    {
        var keys = Ward(5);
        var preview = Plan(Policy(keys, Settings(1), Settings(2), Settings(3)), [Character(1), Character(2), Character(3)], Listed(keys));
        var fallback = preview.Assignments[0].TargetOrder[2];

        var followed = AssignmentPlanner.FollowConfirmedEntry(preview.Assignments, preview.Assignments[0].ContentId, fallback);

        for (var index = 1; index < followed.Length; index++)
        {
            Assert.Equal<PlotKey>([fallback], followed[index].TargetOrder);
        }
    }

    [Fact]
    public void SeparateGroupsSpreadAcrossDistinctPlotsInRankOrder()
    {
        var keys = Ward(6);
        var roster = new[] { Character(1, accountGroup: 1), Character(2, accountGroup: 2), Character(3, accountGroup: 3) };

        var preview = Plan(Policy(keys, Settings(1), Settings(2), Settings(3)), roster, Listed(keys));

        Assert.Equal(preview.Ranking[0].Key, preview.Assignments[0].TargetOrder[0]);
        Assert.Equal(preview.Ranking[1].Key, preview.Assignments[1].TargetOrder[0]);
        Assert.Equal(preview.Ranking[2].Key, preview.Assignments[2].TargetOrder[0]);
        Assert.Empty(preview.GroupOdds);
        Assert.True(preview.CanRun);
    }

    [Fact]
    public void ExactTargetsAreTakenBeforeSpreadHandsOutPlots()
    {
        var keys = Ward(4);
        var roster = new[] { Character(1, accountGroup: 1), Character(2, accountGroup: 2) };
        var policy = Policy(keys, Settings(1), Settings(2) with { Target = keys[0] }) with { Placement = PlacementChoice.Spread };

        var preview = Plan(policy, roster, Listed(keys));

        Assert.Equal(keys[0], Find(preview.Assignments, 2).TargetOrder[0]);
        Assert.Equal(keys[1], Find(preview.Assignments, 1).TargetOrder[0]);
    }

    [Fact]
    public void TwoUngroupedPrivateCharactersOnOneWorldDefaultToSamePlot()
    {
        var keys = Ward(4);

        var preview = Plan(Policy(keys, Settings(1), Settings(2)), [Character(1), Character(2)], Listed(keys));

        Assert.Equal(OwnershipGroupKind.UngroupedHomeWorld, preview.Assignments[0].Group.Kind);
        Assert.Equal(preview.Assignments[0].Group, preview.Assignments[1].Group);
        Assert.Equal(preview.Assignments[0].TargetOrder[0], preview.Assignments[1].TargetOrder[0]);
    }

    [Fact]
    public void SpreadInsideOneGroupIsBlockedUntilConfirmedAndShowsTheMultipleWinEstimate()
    {
        var keys = Ward(4);
        var spread = Policy(keys, Settings(1), Settings(2)) with { Placement = PlacementChoice.Spread };

        var unconfirmed = Plan(spread, [Character(1), Character(2)], Listed(keys, _ => 10));
        var confirmed = Plan(spread with { MultipleWinRiskAccepted = true }, [Character(1), Character(2)], Listed(keys, _ => 10));

        Assert.True(unconfirmed.Blockers.HasFlag(RunBlocker.MultipleWinsNeedConfirmation));
        Assert.NotNull(Assert.Single(unconfirmed.GroupOdds).Estimate);
        Assert.True(confirmed.CanRun);
    }

    [Fact]
    public void ThreeCharactersOnTwoAndTenReportedEntriesMatchThePlanTable()
    {
        var keys = Ward(3);
        var roster = new[] { Character(1), Character(2), Character(3) };
        var characters = new[] { Settings(1), Settings(2), Settings(3) };

        var samePlotTwo = Plan(Policy(keys, characters), roster, Listed(keys, _ => 2));
        var samePlotTen = Plan(Policy(keys, characters), roster, Listed(keys, _ => 10));
        var spreadTwo = Plan(Policy(keys, characters) with { Placement = PlacementChoice.Spread, MultipleWinRiskAccepted = true }, roster, Listed(keys, _ => 2));
        var spreadTen = Plan(Policy(keys, characters) with { Placement = PlacementChoice.Spread, MultipleWinRiskAccepted = true }, roster, Listed(keys, _ => 10));

        Assert.Equal(60, Percent(samePlotTwo.Odds!.Value.AnyWin));
        Assert.Equal(23, Percent(samePlotTen.Odds!.Value.AnyWin));
        Assert.Equal(70, Percent(spreadTwo.Odds!.Value.AnyWin));
        Assert.Equal(25, Percent(spreadTen.Odds!.Value.AnyWin));
        Assert.Equal(26, Percent(Assert.Single(spreadTwo.GroupOdds).Estimate!.Value.TwoOrMoreWins));
        Assert.Equal(2, Percent(Assert.Single(spreadTen.GroupOdds).Estimate!.Value.TwoOrMoreWins));
    }

    [Fact]
    public void AnUnknownCountOnAnyInvolvedPlotHidesTheEstimate()
    {
        var keys = Ward(2);
        var plots = Listed(keys, _ => 5);
        plots[keys[1]] = ListedPlot(keys[1], null);
        var policy = Policy(keys, Settings(1), Settings(2)) with { Placement = PlacementChoice.Spread, MultipleWinRiskAccepted = true };

        var preview = Plan(policy, [Character(1), Character(2)], plots);

        Assert.Null(preview.Odds);
        Assert.Null(Assert.Single(preview.GroupOdds).Estimate);
    }

    [Fact]
    public void EveryCharacterIsCheckedAgainstItsOwnGilAndNothingIsPooled()
    {
        var keys = Ward(2);
        var roster = new[]
        {
            Character(1, accountGroup: 1, gil: Deposit + Reserve),
            Character(2, accountGroup: 2, gil: Deposit + Reserve - 1),
            Character(3, accountGroup: 3, gil: 3 * Deposit),
        };

        var preview = Plan(Policy(keys, Settings(1), Settings(2), Settings(3)), roster, Listed(keys));

        Assert.Equal(2, preview.MaxNewEntries);
        var poor = Assert.Single(preview.Exclusions);
        Assert.Equal(2UL, poor.ContentId);
        Assert.Equal(EligibilityReason.InsufficientGil, poor.Reasons);
        Assert.Equal(2 * Deposit, preview.TotalMaximumDeposit);
    }

    [Fact]
    public void AnOfflineCharactersRememberedGilNeitherProvesNorDisprovesItCanPay()
    {
        var keys = Ward(1);
        var rich = Offline(1, gil: 100 * Deposit);
        var poor = Offline(2, gil: 1);

        var preview = Plan(Policy(keys, Settings(1), Settings(2)), [rich, poor], Listed(keys));

        Assert.Equal(2, preview.MaxNewEntries);
        for (var index = 0; index < preview.Assignments.Length; index++)
        {
            var eligibility = preview.Assignments[index].Eligibility;
            Assert.Equal(EligibilityVerdict.Unknown, eligibility.Verdict);
            Assert.True(eligibility.Unresolved.HasFlag(EligibilityReason.InsufficientGil));
        }
    }

    [Fact]
    public void AnOfflineCharacterAlreadyEnteredInTheJournalIsLeftOut()
    {
        var keys = Ward(2);
        var receipt = new EntryReceipt
        {
            ReceiptId = Guid.NewGuid(),
            CharacterContentId = 2,
            HomeWorldId = HomeWorld,
            Cycle = FirstCycle,
            Plot = keys[1],
            Buyer = BuyerType.Private,
            Evidence = ReceiptEvidence.NativeConfirmation,
            Deposit = Deposit,
            EnteredUtc = ObservedAt,
        };
        var journal = PlanningJournal.Empty with { Snapshot = new JournalSnapshot([], [receipt], [EntrySettlement.Entered(receipt.ReceiptId, ObservedAt)]) };
        var entered = RosterProfiles.Offline(Facts(2, gil: 10 * Deposit), GameRegion.Global, journal, VerifiedEntryPhase());

        var preview = Plan(Policy(keys, Settings(1), Settings(2)), [Character(1), entered], Listed(keys));

        Assert.Equal(1, preview.MaxNewEntries);
        Assert.Equal(EligibilityReason.AlreadyEnteredThisCycle, Assert.Single(preview.Exclusions).Reasons);
    }

    [Fact]
    public void ACharacterOnAnUnsupportedRegionIsLeftOutWithThatReason()
    {
        var keys = Ward(2);
        var korean = RosterProfiles.Offline(Facts(2) with { RegionUnsupported = true }, GameRegion.Global, Empty, VerifiedEntryPhase());

        var preview = Plan(Policy(keys, Settings(1), Settings(2)), [Character(1), korean], Listed(keys));

        Assert.Equal(1, preview.MaxNewEntries);
        Assert.True(Assert.Single(preview.Exclusions).Reasons.HasFlag(EligibilityReason.UnsupportedRegion));
    }

    [Fact]
    public void ACharacterTheAccountKnowsOwnsLandIsLeftOutWithThatReason()
    {
        var keys = Ward(2);
        var owner = RosterProfiles.Offline(Facts(2, ownsLand: true), GameRegion.Global, Empty, VerifiedEntryPhase());

        var preview = Plan(Policy(keys, Settings(1), Settings(2)), [Character(1), owner], Listed(keys));

        Assert.True(Assert.Single(preview.Exclusions).Reasons.HasFlag(EligibilityReason.AccountOwnsLandOnWorld));
    }

    [Fact]
    public void TheBatchPolicyGivesEachCharacterItsOwnSettingsOrTheShortlistDefaults()
    {
        var keys = Ward(3);
        RosterEntry[] selected =
        [
            new(Facts(1), new RosterSelection(true, BuyerType.Unknown, 0, null, null)),
            new(Facts(2), new RosterSelection(true, BuyerType.FreeCompany, 2_000_000, 50_000, keys[2])) { ManualRefunds = true },
        ];
        var shortlist = new ShortlistSnapshot(Guid.NewGuid(), 4, [.. keys]);

        var policy = RunPreviewBuilder.BatchPolicy(selected, BuyerType.Private, 4_000_000, 300_000, shortlist, VerifiedEntryPhase(), PlacementChoice.Spread, multipleWinRiskAccepted: false);

        Assert.Equal(new CharacterRunSettings(1, BuyerType.Private, 4_000_000, 300_000), policy.Characters[0]);
        Assert.Equal(new CharacterRunSettings(2, BuyerType.FreeCompany, 2_000_000, 50_000) { Target = keys[2], ManualRefunds = true }, policy.Characters[1]);
        Assert.Equal(PlacementChoice.Spread, policy.Placement);
        Assert.Equal(RunPolicyError.None, RunPolicyRules.Validate(policy));
    }

    [Fact]
    public void AnyTickedCharacterMakesABatchOfExactlyTheTickedOnes()
    {
        RosterEntry[] alone = [new(Facts(1), new RosterSelection(true, BuyerType.Unknown, 0, null, null))];

        Assert.False(RunPreviewBuilder.IsBatch([]));
        Assert.True(RunPreviewBuilder.IsBatch(alone));
    }

    internal static RosterFacts Facts(ulong contentId, long? gil = null, bool? ownsLand = null, int? accountGroup = null)
        => new(contentId, $"Character {contentId}", HomeWorld, accountGroup, 900, ownsLand, gil, gil is null ? null : ObservedAt, ObservedAt);

    private static readonly PlanningJournal Empty = PlanningJournal.Empty;

    private static CharacterProfile Offline(ulong contentId, long gil)
        => RosterProfiles.Offline(Facts(contentId, gil), GameRegion.Global, Empty, VerifiedEntryPhase()) with
        {
            Participation = new CycleParticipation(FirstCycle, EntrySlotState.Open),
            Outstanding = OutstandingSettlement.None,
            PrivateBuyerQualified = true,
            AccountOwnsLandOnHomeWorld = false,
        };

    private static int Percent(double value) => (int)Math.Round(value * 100);

    private static int CountReason(RunPreview preview, EligibilityReason reason)
    {
        var count = 0;
        for (var index = 0; index < preview.Exclusions.Length; index++)
        {
            count += (preview.Exclusions[index].Reasons & reason) != 0 ? 1 : 0;
        }

        return count;
    }

    private static RunAssignment Find(ImmutableArray<RunAssignment> assignments, ulong contentId)
    {
        for (var index = 0; index < assignments.Length; index++)
        {
            if (assignments[index].ContentId == contentId)
            {
                return assignments[index];
            }
        }

        throw new InvalidOperationException($"No assignment for {contentId}.");
    }

    private static RunPreview Plan(RunPolicy policy, ReadOnlySpan<CharacterProfile> roster, IReadOnlyDictionary<PlotKey, PlotSnapshot> plots)
        => AssignmentPlanner.Build(policy, roster, plots, now);

    internal static RunPolicy Policy(PlotKey[] keys, params CharacterRunSettings[] characters) => new()
    {
        Revision = 1,
        Cycle = VerifiedEntryPhase(),
        Characters = [.. characters],
        TravelAllowance = 0,
        Shortlist = new ShortlistSnapshot(Guid.NewGuid(), 1, [.. keys]),
    };

    internal static CharacterRunSettings Settings(ulong contentId, BuyerType buyer = BuyerType.Private) => new(contentId, buyer, Deposit, Reserve);

    internal static PlotKey[] Ward(int plotCount)
    {
        var keys = new PlotKey[plotCount];
        for (var index = 0; index < plotCount; index++)
        {
            keys[index] = Plot(1, index + 1);
        }

        return keys;
    }

    internal static Dictionary<PlotKey, PlotSnapshot> Listed(PlotKey[] keys, Func<int, int?>? entries = null)
    {
        var plots = new Dictionary<PlotKey, PlotSnapshot>();
        for (var index = 0; index < keys.Length; index++)
        {
            plots[keys[index]] = ListedPlot(keys[index], entries is null ? index : entries(index));
        }

        return plots;
    }
}
