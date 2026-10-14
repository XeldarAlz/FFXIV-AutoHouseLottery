using AutoHouseLottery.Core.Batch.Model;
using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Selection.Model;
using AutoHouseLottery.Core.Tasks.Model;
using static AutoHouseLottery.Tests.Batch.BatchPlanningTests;
using static AutoHouseLottery.Tests.CharacterFixtures;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests.Batch;

public sealed class BatchRulesTests
{
    private static readonly DateTime now = CycleStart.AddHours(12);
    private static readonly JournalSnapshot emptyJournal = new([], [], []);

    [Fact]
    public void TheLoggedInCharacterGoesFirstWithoutASwitch()
    {
        var batch = Batch(out _);

        var step = BatchRules.Next(batch, loggedIn: 3, switchingAllowed: true);

        Assert.Equal(BatchStepKind.Process, step.Kind);
        Assert.Equal(3UL, batch.Members[step.MemberIndex].ContentId);
    }

    [Fact]
    public void WithoutSwitchingTheOthersWaitForTheirOwnLogin()
    {
        var batch = Batch(out _);
        batch = Done(batch, 1, BatchMemberState.DryRun);

        var step = BatchRules.Next(batch, loggedIn: 1, switchingAllowed: false);

        Assert.Equal(BatchStepKind.WaitForLogin, step.Kind);
        Assert.Equal(2UL, batch.Members[step.MemberIndex].ContentId);
    }

    [Fact]
    public void WithSwitchingTheNextWaitingMemberIsLoggedIn()
    {
        var batch = Done(Batch(out _), 1, BatchMemberState.DryRun);

        var step = BatchRules.Next(batch, loggedIn: 1, switchingAllowed: true);

        Assert.Equal(BatchStepKind.Switch, step.Kind);
        Assert.Equal(2UL, batch.Members[step.MemberIndex].ContentId);
    }

    [Fact]
    public void ABatchWithNobodyWaitingIsFinished()
    {
        var batch = Batch(out _);
        for (var contentId = 1UL; contentId <= 3; contentId++)
        {
            batch = Done(batch, contentId, BatchMemberState.NotEntered);
        }

        Assert.Equal(BatchStepKind.Finished, BatchRules.Next(batch, 1, switchingAllowed: true).Kind);
    }

    [Fact]
    public void AnExcludedCharacterIsNeverProcessedOrSwitchedTo()
    {
        var keys = Ward(2);
        var roster = new[] { Character(1), Character(2) with { AccountOwnsLandOnHomeWorld = true } };
        var policy = Policy(keys, Settings(1), Settings(2));
        var batch = BatchRules.Create(Guid.NewGuid(), policy, AssignmentPlanner.Build(policy, roster, Listed(keys), now), roster, dryRunRequested: true, now);
        batch = Done(batch, 1, BatchMemberState.DryRun);

        Assert.Equal(BatchMemberState.Excluded, batch.Members[batch.IndexOf(2)].State);
        Assert.Equal(BatchStepKind.Finished, BatchRules.Next(batch, 2, switchingAllowed: true).Kind);
    }

    [Theory]
    [InlineData((byte)EntryStep.Completed, (byte)EntryFailure.None, (byte)BatchEnd.Finished, true, (byte)BatchMemberState.DryRun, (byte)BatchHalt.None)]
    [InlineData((byte)EntryStep.Completed, (byte)EntryFailure.None, (byte)BatchEnd.Finished, false, (byte)BatchMemberState.Entered, (byte)BatchHalt.None)]
    [InlineData((byte)EntryStep.Completed, (byte)EntryFailure.None, (byte)BatchEnd.StoppedByPlayer, false, (byte)BatchMemberState.Entered, (byte)BatchHalt.StoppedByPlayer)]
    [InlineData((byte)EntryStep.CompletedPartial, (byte)EntryFailure.InspectionsExhausted, (byte)BatchEnd.Finished, false, (byte)BatchMemberState.NotEntered, (byte)BatchHalt.None)]
    [InlineData((byte)EntryStep.NeedsReconciliation, (byte)EntryFailure.SubmissionUncertain, (byte)BatchEnd.Finished, false, (byte)BatchMemberState.Uncertain, (byte)BatchHalt.NeedsReconciliation)]
    [InlineData((byte)EntryStep.NeedsReconciliation, (byte)EntryFailure.None, (byte)BatchEnd.Interrupted, false, (byte)BatchMemberState.Uncertain, (byte)BatchHalt.NeedsReconciliation)]
    [InlineData((byte)EntryStep.Stopped, (byte)EntryFailure.TravelFailed, (byte)BatchEnd.StoppedByPlayer, false, (byte)BatchMemberState.Queued, (byte)BatchHalt.StoppedByPlayer)]
    [InlineData((byte)EntryStep.Stopped, (byte)EntryFailure.TravelFailed, (byte)BatchEnd.Interrupted, false, (byte)BatchMemberState.Queued, (byte)BatchHalt.Interrupted)]
    [InlineData((byte)EntryStep.Stopped, (byte)EntryFailure.CharacterChanged, (byte)BatchEnd.Finished, false, (byte)BatchMemberState.Queued, (byte)BatchHalt.CharacterChanged)]
    [InlineData((byte)EntryStep.Stopped, (byte)EntryFailure.AutoAnswerPlugin, (byte)BatchEnd.Finished, false, (byte)BatchMemberState.Queued, (byte)BatchHalt.AutoAnswerPlugin)]
    [InlineData((byte)EntryStep.Stopped, (byte)EntryFailure.Rejected, (byte)BatchEnd.Finished, false, (byte)BatchMemberState.NotEntered, (byte)BatchHalt.None)]
    public void ARunsEndDecidesTheMemberAndWhetherTheBatchGoesOn(byte step, byte failure, byte end, bool traced, byte state, byte halt)
    {
        Assert.Equal(((BatchMemberState)state, (BatchHalt)halt), BatchRules.Classify((EntryStep)step, (EntryFailure)failure, (BatchEnd)end, traced));
    }

    [Fact]
    public void AnEntryOnSamePlotPinsTheGroupMembersStillWaiting()
    {
        var batch = Batch(out _);
        var lead = batch.Members[0];
        var fallback = lead.Assignment!.Value.TargetOrder[1];

        var recorded = BatchRules.Record(batch, 0, BatchMemberState.Entered, EntryFailure.None, fallback, Deposit, now);

        for (var index = 1; index < recorded.Members.Length; index++)
        {
            Assert.Equal<PlotKey>([fallback], recorded.Members[index].Assignment!.Value.TargetOrder);
        }
    }

    [Fact]
    public void AMemberThatAlreadyRanKeepsItsOwnAssignment()
    {
        var batch = Done(Batch(out _), 2, BatchMemberState.NotEntered);
        var before = batch.Members[batch.IndexOf(2)].Assignment;
        var fallback = batch.Members[0].Assignment!.Value.TargetOrder[1];

        var recorded = BatchRules.Record(batch, 0, BatchMemberState.DryRun, EntryFailure.None, fallback, Deposit, now);

        Assert.Equal(before, recorded.Members[recorded.IndexOf(2)].Assignment);
    }

    [Fact]
    public void SpreadMembersAreNotPinnedByAnotherEntry()
    {
        var keys = Ward(4);
        var roster = new[] { Character(1, accountGroup: 1), Character(2, accountGroup: 2) };
        var policy = Policy(keys, Settings(1), Settings(2)) with { Placement = PlacementChoice.Spread };
        var batch = BatchRules.Create(Guid.NewGuid(), policy, AssignmentPlanner.Build(policy, roster, Listed(keys), now), roster, true, now);
        var before = batch.Members[1].Assignment;

        var recorded = BatchRules.Record(batch, 0, BatchMemberState.Entered, EntryFailure.None, batch.Members[0].Assignment!.Value.TargetOrder[0], Deposit, now);

        Assert.Equal(before, recorded.Members[1].Assignment);
    }

    [Fact]
    public void AHaltPutsTheRunningMemberBackInTheQueue()
    {
        var batch = BatchRules.Begin(Batch(out _), 1, now);

        var halted = BatchRules.Halt(batch, BatchHalt.StoppedByPlayer, now);

        Assert.Equal(BatchPhase.Halted, halted.Phase);
        Assert.Equal(BatchMemberState.Queued, halted.Members[1].State);
        Assert.Null(halted.SwitchTarget);
    }

    [Fact]
    public void AReloadNeverResumesARunningOrSwitchingBatchOnItsOwn()
    {
        var switching = BatchRules.BeginSwitch(Done(Batch(out _), 1, BatchMemberState.DryRun), 2, now);
        var waiting = BatchRules.Wait(switching, now);

        var restored = BatchRules.Restore(switching, now);

        Assert.Equal(BatchPhase.Halted, restored.Phase);
        Assert.Equal(BatchHalt.Reloaded, restored.Halt);
        Assert.Null(restored.SwitchTarget);
        Assert.Same(waiting, BatchRules.Restore(waiting, now));
    }

    [Fact]
    public void ANewCycleEndsTheBatchAndReportsTheWaitingAsNotEntered()
    {
        var batch = Done(Batch(out _), 1, BatchMemberState.Entered);
        var nextCycle = VerifiedEntryPhase() with { Cycle = SecondCycle };

        Assert.True(BatchRules.Expired(batch, nextCycle, now));
        Assert.True(BatchRules.Expired(batch, VerifiedEntryPhase(), VerifiedEntryPhase().PhaseEndUtc));
        Assert.False(BatchRules.Expired(batch, VerifiedEntryPhase(), now));
        var expired = BatchRules.Expire(batch, now);
        Assert.Equal(BatchPhase.Finished, expired.Phase);
        Assert.Equal(BatchHalt.CycleEnded, expired.Halt);
        Assert.Equal(BatchMemberState.Entered, expired.Members[expired.IndexOf(1)].State);
        Assert.Equal(EntryFailure.CycleChanged, expired.Members[expired.IndexOf(2)].Failure);
    }

    [Fact]
    public void OnlyTheRequestedCharacterOnItsHomeWorldCountsAfterASwitch()
    {
        var switching = BatchRules.BeginSwitch(Done(Batch(out _), 1, BatchMemberState.DryRun), 2, now);

        Assert.Equal(LoginVerdict.RequestedCharacter, BatchRules.OnLogin(switching, 2, HomeWorld));
        Assert.Equal(LoginVerdict.WrongCharacter, BatchRules.OnLogin(switching, 3, HomeWorld));
        Assert.Equal(LoginVerdict.WrongCharacter, BatchRules.OnLogin(switching, 1, HomeWorld));
        Assert.Equal(LoginVerdict.WrongHomeWorld, BatchRules.OnLogin(switching, 2, OtherWorld));
    }

    [Fact]
    public void ANormalLoginOfAWaitingMemberIsRecognised()
    {
        var waiting = BatchRules.Wait(Done(Batch(out _), 1, BatchMemberState.DryRun), now);

        Assert.Equal(LoginVerdict.QueuedCharacter, BatchRules.OnLogin(waiting, 3, HomeWorld));
        Assert.Equal(LoginVerdict.NotInBatch, BatchRules.OnLogin(waiting, 1, HomeWorld));
        Assert.Equal(LoginVerdict.NotInBatch, BatchRules.OnLogin(waiting, 9, HomeWorld));
    }

    [Fact]
    public void ThePartialReportCountsEveryOutcomeSoExcludedAndPoorCharactersHideNoEntry()
    {
        var keys = Ward(3);
        var roster = new[]
        {
            Character(1, accountGroup: 1),
            Character(2, accountGroup: 2),
            Character(3, accountGroup: 3),
            Character(4, accountGroup: 4) with { AccountOwnsLandOnHomeWorld = true },
        };
        var policy = Policy(keys, Settings(1), Settings(2), Settings(3), Settings(4));
        var batch = BatchRules.Create(Guid.NewGuid(), policy, AssignmentPlanner.Build(policy, roster, Listed(keys), now), roster, false, now);
        batch = BatchRules.Record(batch, batch.IndexOf(1), BatchMemberState.Entered, EntryFailure.None, keys[0], Deposit, now);
        batch = BatchRules.Skip(batch, batch.IndexOf(2), EligibilityReason.InsufficientGil, EntryFailure.OverBudget, null, now);

        var report = BatchRules.Report(batch);

        Assert.Equal(new BatchReport(1, 0, 1, 1, 0, 1, 0, Deposit), report);
        Assert.True(BatchRules.DisplayRank(BatchMemberState.Entered) < BatchRules.DisplayRank(BatchMemberState.NotEntered));
        Assert.True(BatchRules.DisplayRank(BatchMemberState.Entered) < BatchRules.DisplayRank(BatchMemberState.Excluded));
    }

    [Fact]
    public void EachMemberReservesItsOwnMaximumAgainstItsOwnCapAndGilSoNothingIsPooled()
    {
        var keys = Ward(2);
        var roster = new[] { Character(1, accountGroup: 1), Character(2, accountGroup: 2) };
        var policy = Policy(keys, Settings(1), Settings(2));
        var batch = BatchRules.Create(Guid.NewGuid(), policy, AssignmentPlanner.Build(policy, roster, Listed(keys), now), roster, true, now);
        var first = batch.Members[batch.IndexOf(1)];
        var second = batch.Members[batch.IndexOf(2)];
        var firstBudget = new CharacterBudget(first.Settings.DepositCap, first.Settings.GilReserve, 0);
        var ownGil = Deposit + first.Settings.GilReserve;

        Assert.True(default(DepositLedger).TryReserve(first.Assignment!.Value.MaximumDeposit, out var firstLedger));
        Assert.True(default(DepositLedger).TryReserve(second.Assignment!.Value.MaximumDeposit, out _));
        Assert.True(firstLedger.TryReserve(second.Assignment.Value.MaximumDeposit, out var pooled));
        Assert.Equal(BudgetCheck.Fits, BudgetRules.Check(firstBudget, default, Deposit, ownGil));
        Assert.Equal(BudgetCheck.OverDepositCap, BudgetRules.Check(firstBudget, pooled, Deposit, 10 * ownGil));
        Assert.Equal(BudgetCheck.Fits, BatchRules.Affordable(firstBudget, first.Assignment.Value.TargetOrder, batch.Ranking, ownGil));
        Assert.Equal(BudgetCheck.BelowReserve, BatchRules.Affordable(firstBudget, first.Assignment.Value.TargetOrder, batch.Ranking, ownGil - 1));
    }

    [Fact]
    public void TheFreshBalanceIsCheckedAgainstTheWholeTargetOrder()
    {
        PlotKey[] targets = [Plot(1, 1), Plot(1, 2), Plot(1, 3)];
        System.Collections.Immutable.ImmutableArray<RankedPlot> ranking =
        [
            new(targets[0], 1, 3 * Deposit),
            new(targets[1], 2, Deposit),
            new(targets[2], 3, null),
        ];
        var budget = new CharacterBudget(3 * Deposit, 0, 0);

        Assert.Equal(BudgetCheck.Fits, BatchRules.Affordable(budget, [targets[0], targets[1]], ranking, Deposit));
        Assert.Equal(BudgetCheck.BelowReserve, BatchRules.Affordable(budget, [targets[0], targets[1]], ranking, Deposit - 1));
        Assert.Equal(BudgetCheck.DepositUnknown, BatchRules.Affordable(budget, [targets[0], targets[2]], ranking, Deposit));
        Assert.Equal(BudgetCheck.BalanceUnknown, BatchRules.Affordable(budget, [targets[0], targets[1]], ranking, null));
        Assert.Equal(BudgetCheck.OverDepositCap, BatchRules.Affordable(budget with { DepositCap = Deposit - 1 }, [targets[0], targets[1]], ranking, 10 * Deposit));
    }

    [Fact]
    public void AFreshCheckNamesWhyAMemberIsLeftOut()
    {
        Assert.Equal((EligibilityReason.None, EntryFailure.None), BatchRules.Recheck(EligibilityReason.None, BudgetCheck.Fits));
        Assert.Equal((EligibilityReason.None, EntryFailure.None), BatchRules.Recheck(EligibilityReason.None, BudgetCheck.DepositUnknown));
        Assert.Equal((EligibilityReason.InsufficientGil, EntryFailure.OverBudget), BatchRules.Recheck(EligibilityReason.None, BudgetCheck.BelowReserve));
        Assert.Equal((EligibilityReason.DepositOverCap, EntryFailure.OverBudget), BatchRules.Recheck(EligibilityReason.None, BudgetCheck.OverDepositCap));
        Assert.Equal(EntryFailure.AlreadyEntered, BatchRules.Recheck(EligibilityReason.AlreadyEnteredThisCycle, BudgetCheck.BelowReserve).Failure);
        Assert.Equal(EntryFailure.CollectRefundFirst, BatchRules.Recheck(EligibilityReason.OutstandingRefundOrProperty, BudgetCheck.Fits).Failure);
        Assert.Equal(EntryFailure.CharacterIneligible, BatchRules.Recheck(EligibilityReason.AccountOwnsLandOnWorld, BudgetCheck.Fits).Failure);
    }

    [Fact]
    public void WithAnExactMemberTheLoggedInCharacterGoesFirstButNeverFallsBackToAnotherPlot()
    {
        var keys = Ward(5);
        var roster = new[] { Character(1), Character(2), Character(3) };
        var policy = Policy(keys, Settings(1) with { Target = keys[3] }, Settings(2), Settings(3));
        var batch = BatchRules.Create(Guid.NewGuid(), policy, AssignmentPlanner.Build(policy, roster, Listed(keys), now), roster, true, now);

        var first = BatchRules.Next(batch, loggedIn: 2, switchingAllowed: true);
        Assert.Equal(2UL, batch.Members[first.MemberIndex].ContentId);
        Assert.Equal<PlotKey>([keys[3]], batch.Members[first.MemberIndex].Assignment!.Value.TargetOrder);
        batch = BatchRules.Record(batch, first.MemberIndex, BatchMemberState.NotEntered, EntryFailure.ChangedPrice, null, null, now);
        batch = BatchRules.Record(batch, batch.IndexOf(3), BatchMemberState.DryRun, EntryFailure.None, keys[3], Deposit, now);

        var exact = batch.Members[batch.IndexOf(1)];
        Assert.Equal(BatchMemberState.Queued, exact.State);
        Assert.Equal<PlotKey>([keys[3]], exact.Assignment!.Value.TargetOrder);
    }

    [Fact]
    public void AnEntryLeavesOutEveryWaitingGroupMemberWhosePlanLacksThatPlot()
    {
        var batch = Batch(out _);
        var lead = batch.Members[0].Assignment!.Value;
        var entered = lead.TargetOrder[0];
        var other = lead.TargetOrder[1];
        var lacking = batch.Members[1] with { Assignment = batch.Members[1].Assignment!.Value with { TargetOrder = [other] } };
        var exactElsewhere = batch.Members[2] with { Assignment = batch.Members[2].Assignment!.Value with { TargetOrder = [other], Exact = true } };
        batch = batch with { Members = [batch.Members[0], lacking, exactElsewhere] };

        var recorded = BatchRules.Record(batch, 0, BatchMemberState.Entered, EntryFailure.None, entered, Deposit, now);

        Assert.Equal(BatchMemberState.NotEntered, recorded.Members[1].State);
        Assert.Equal(EntryFailure.GroupEnteredElsewhere, recorded.Members[1].Failure);
        Assert.Equal<PlotKey>([other], recorded.Members[1].Assignment!.Value.TargetOrder);
        Assert.Equal(BatchMemberState.NotEntered, recorded.Members[2].State);
        Assert.Equal(EntryFailure.GroupEnteredElsewhere, recorded.Members[2].Failure);
    }

    [Fact]
    public void AnEntryInQuestionIsLookedAtAgainFromTheJournalOnContinue()
    {
        var batch = Batch(out _);
        var cycle = batch.Policy.Cycle.Cycle;
        var plot = batch.Members[0].Assignment!.Value.TargetOrder[0];
        batch = BatchRules.Record(batch, 0, BatchMemberState.Uncertain, EntryFailure.SubmissionUncertain, null, null, now);
        batch = BatchRules.Record(batch, 1, BatchMemberState.Uncertain, EntryFailure.SubmissionUncertain, null, null, now);
        batch = BatchRules.Record(batch, 2, BatchMemberState.Uncertain, EntryFailure.SubmissionUncertain, null, null, now);
        var receipt = new EntryReceipt
        {
            ReceiptId = Guid.NewGuid(),
            CharacterContentId = batch.Members[0].ContentId,
            HomeWorldId = HomeWorld,
            Cycle = cycle,
            Plot = plot,
            Buyer = BuyerType.Private,
            Evidence = ReceiptEvidence.Reconciled,
            Deposit = Deposit,
            EnteredUtc = ObservedAt,
        };
        var pending = new ActionAttempt
        {
            AttemptId = Guid.NewGuid(),
            Kind = ActionKind.Entry,
            RunId = Guid.NewGuid(),
            CharacterContentId = batch.Members[2].ContentId,
            HomeWorldId = HomeWorld,
            Cycle = cycle,
            Plot = plot,
            Buyer = BuyerType.Private,
            Amount = Deposit,
            PreparedUtc = ObservedAt,
            State = AttemptState.NeedsReconciliation,
            UpdatedUtc = ObservedAt,
        };
        var journal = new JournalSnapshot([pending], [receipt], [EntrySettlement.Entered(receipt.ReceiptId, ObservedAt)]);

        var reevaluated = BatchRules.Reevaluate(batch, journal, now);

        Assert.Equal(BatchMemberState.Entered, reevaluated.Members[0].State);
        Assert.Equal(plot, reevaluated.Members[0].Plot);
        Assert.Equal(Deposit, reevaluated.Members[0].Deposit);
        Assert.Equal(BatchMemberState.Queued, reevaluated.Members[1].State);
        Assert.Equal<PlotKey>([plot], reevaluated.Members[1].Assignment!.Value.TargetOrder);
        Assert.Equal(BatchMemberState.Uncertain, reevaluated.Members[2].State);
    }

    [Fact]
    public void AMemberFreedOnContinueFollowsThePlotItsGroupEnteredWhileItWasInQuestion()
    {
        var batch = Batch(out _);
        var entered = batch.Members[0].Assignment!.Value.TargetOrder[1];
        batch = BatchRules.Record(batch, 0, BatchMemberState.Uncertain, EntryFailure.SubmissionUncertain, null, null, now);
        batch = BatchRules.Record(batch, 1, BatchMemberState.Entered, EntryFailure.None, entered, Deposit, now);

        var reevaluated = BatchRules.Reevaluate(batch, emptyJournal, now);

        Assert.Equal(BatchMemberState.Queued, reevaluated.Members[0].State);
        Assert.Equal<PlotKey>([entered], reevaluated.Members[0].Assignment!.Value.TargetOrder);
        Assert.Equal<PlotKey>([entered], reevaluated.Members[2].Assignment!.Value.TargetOrder);
    }

    [Fact]
    public void AMemberFreedOnContinueWhosePlanLacksItsGroupsPlotIsLeftOut()
    {
        var batch = Batch(out _);
        var targets = batch.Members[0].Assignment!.Value.TargetOrder;
        batch = batch with { Members = batch.Members.SetItem(0, batch.Members[0] with { Assignment = batch.Members[0].Assignment!.Value with { TargetOrder = [targets[0]] } }) };
        batch = BatchRules.Record(batch, 0, BatchMemberState.Uncertain, EntryFailure.SubmissionUncertain, null, null, now);
        batch = BatchRules.Record(batch, 1, BatchMemberState.DryRun, EntryFailure.None, targets[1], Deposit, now);

        var reevaluated = BatchRules.Reevaluate(batch, emptyJournal, now);

        Assert.Equal(BatchMemberState.NotEntered, reevaluated.Members[0].State);
        Assert.Equal(EntryFailure.GroupEnteredElsewhere, reevaluated.Members[0].Failure);
    }

    [Fact]
    public void GroupMatesOfAMemberInQuestionWaitWhileOtherGroupsGoOn()
    {
        var batch = TwoGroups();
        batch = BatchRules.Record(batch, batch.IndexOf(1), BatchMemberState.Uncertain, EntryFailure.SubmissionUncertain, null, null, now);

        var mateLoggedIn = BatchRules.Next(batch, loggedIn: 2, switchingAllowed: true);
        var mateWaitsForLogin = BatchRules.Next(batch, loggedIn: 2, switchingAllowed: false);

        Assert.True(BatchRules.Held(batch, batch.IndexOf(2)));
        Assert.False(BatchRules.Held(batch, batch.IndexOf(3)));
        Assert.Equal(BatchStepKind.Switch, mateLoggedIn.Kind);
        Assert.Equal(3UL, batch.Members[mateLoggedIn.MemberIndex].ContentId);
        Assert.Equal(BatchStepKind.WaitForLogin, mateWaitsForLogin.Kind);
        Assert.Equal(3UL, batch.Members[mateWaitsForLogin.MemberIndex].ContentId);

        batch = Done(batch, 3, BatchMemberState.DryRun);
        var onlyHeld = BatchRules.Next(batch, loggedIn: 2, switchingAllowed: true);

        Assert.Equal(BatchStepKind.Held, onlyHeld.Kind);
        Assert.Equal(2UL, batch.Members[onlyHeld.MemberIndex].ContentId);
        Assert.Equal(StartRefusal.ReconciliationPending, BatchRules.ContinueRefusal(onlyHeld.Kind));
        Assert.Equal(StartRefusal.None, BatchRules.ContinueRefusal(BatchStepKind.Process));
        Assert.Equal(StartRefusal.None, BatchRules.ContinueRefusal(BatchStepKind.Switch));
        Assert.Equal(StartRefusal.CharacterNotInPolicy, BatchRules.ContinueRefusal(BatchStepKind.WaitForLogin));
    }

    [Fact]
    public void ACheckThatFindsTheEntryMadeReleasesItsGroupOntoThatPlot()
    {
        var batch = TwoGroups();
        var first = batch.IndexOf(1);
        var plot = batch.Members[first].Assignment!.Value.TargetOrder[0];
        batch = BatchRules.Record(batch, first, BatchMemberState.Uncertain, EntryFailure.SubmissionUncertain, null, null, now);
        var receipt = Receipt(1, batch.Policy.Cycle.Cycle, plot);

        var reevaluated = BatchRules.Reevaluate(batch, new JournalSnapshot([], [receipt], [EntrySettlement.Entered(receipt.ReceiptId, ObservedAt)]), now);

        Assert.Equal(BatchMemberState.Entered, reevaluated.Members[first].State);
        Assert.False(BatchRules.Held(reevaluated, reevaluated.IndexOf(2)));
        Assert.Equal<PlotKey>([plot], reevaluated.Members[reevaluated.IndexOf(2)].Assignment!.Value.TargetOrder);
        Assert.Equal(BatchStepKind.Process, BatchRules.Next(reevaluated, loggedIn: 2, switchingAllowed: false).Kind);
    }

    [Fact]
    public void AMemberAFreshCheckFindsAlreadyEnteredHoldsItsGroupOnThatPlot()
    {
        var batch = Batch(out _);
        var plot = batch.Members[0].Assignment!.Value.TargetOrder[2];

        var skipped = BatchRules.Skip(batch, 0, EligibilityReason.AlreadyEnteredThisCycle, EntryFailure.AlreadyEntered, plot, now);

        Assert.Equal(BatchMemberState.NotEntered, skipped.Members[0].State);
        Assert.Equal(plot, skipped.Members[0].Plot);
        Assert.Equal<PlotKey>([plot], skipped.Members[1].Assignment!.Value.TargetOrder);
        Assert.Equal<PlotKey>([plot], skipped.Members[2].Assignment!.Value.TargetOrder);
    }

    [Fact]
    public void AMemberItsOwnRunFindsAlreadyEnteredHoldsItsGroupOnThatPlot()
    {
        var batch = Batch(out _);
        var plot = batch.Members[0].Assignment!.Value.TargetOrder[1];

        var recorded = BatchRules.Record(batch, 0, BatchMemberState.NotEntered, EntryFailure.AlreadyEntered, plot, null, now);

        Assert.Equal<PlotKey>([plot], recorded.Members[1].Assignment!.Value.TargetOrder);
        Assert.Equal<PlotKey>([plot], recorded.Members[2].Assignment!.Value.TargetOrder);
    }

    [Fact]
    public void AnEntryOnAPlotThatCouldNotBeReadLeavesTheWaitingGroupOut()
    {
        var batch = Batch(out _);

        var skipped = BatchRules.Skip(batch, 0, EligibilityReason.AlreadyEnteredThisCycle, EntryFailure.AlreadyEntered, null, now);
        var found = BatchRules.Record(batch, 0, BatchMemberState.NotEntered, EntryFailure.AlreadyEntered, null, null, now);

        for (var index = 1; index < batch.Members.Length; index++)
        {
            Assert.Equal(BatchMemberState.NotEntered, skipped.Members[index].State);
            Assert.Equal(EntryFailure.GroupEnteredElsewhere, skipped.Members[index].Failure);
            Assert.Equal(BatchMemberState.NotEntered, found.Members[index].State);
            Assert.Equal(EntryFailure.GroupEnteredElsewhere, found.Members[index].Failure);
        }
    }

    [Fact]
    public void AnotherReasonToSkipNeverMovesTheGroup()
    {
        var batch = Batch(out _);
        var before = batch.Members[1].Assignment;

        var skipped = BatchRules.Skip(batch, 0, EligibilityReason.InsufficientGil, EntryFailure.OverBudget, batch.Members[0].Assignment!.Value.TargetOrder[1], now);

        Assert.Null(skipped.Members[0].Plot);
        Assert.Equal(before, skipped.Members[1].Assignment);
        Assert.Equal(BatchMemberState.Queued, skipped.Members[1].State);
    }

    // Three ungrouped private characters on one world: one ownership group, Same plot, in policy order.
    internal static BatchCheckpoint Batch(out RunPolicy policy)
    {
        var keys = Ward(5);
        var roster = new[] { Character(1), Character(2), Character(3) };
        policy = Policy(keys, Settings(1), Settings(2), Settings(3));
        return BatchRules.Create(Guid.NewGuid(), policy, AssignmentPlanner.Build(policy, roster, Listed(keys), now), roster, dryRunRequested: true, now);
    }

    // Characters 1 and 2 share an ownership group; character 3 is alone in another.
    private static BatchCheckpoint TwoGroups()
    {
        var keys = Ward(5);
        var roster = new[] { Character(1, accountGroup: 1), Character(2, accountGroup: 1), Character(3, accountGroup: 2) };
        var policy = Policy(keys, Settings(1), Settings(2), Settings(3));
        return BatchRules.Create(Guid.NewGuid(), policy, AssignmentPlanner.Build(policy, roster, Listed(keys), now), roster, dryRunRequested: true, now);
    }

    private static EntryReceipt Receipt(ulong contentId, CycleKey cycle, PlotKey plot) => new()
    {
        ReceiptId = Guid.NewGuid(),
        CharacterContentId = contentId,
        HomeWorldId = HomeWorld,
        Cycle = cycle,
        Plot = plot,
        Buyer = BuyerType.Private,
        Evidence = ReceiptEvidence.Reconciled,
        Deposit = Deposit,
        EnteredUtc = ObservedAt,
    };

    // An entry or a dry run always knows its plot: the member's first target here.
    private static BatchCheckpoint Done(BatchCheckpoint batch, ulong contentId, BatchMemberState state)
    {
        var index = batch.IndexOf(contentId);
        var plot = state is BatchMemberState.Entered or BatchMemberState.DryRun ? batch.Members[index].Assignment!.Value.TargetOrder[0] : (PlotKey?)null;
        return BatchRules.Record(batch, index, state, state == BatchMemberState.NotEntered ? EntryFailure.Sold : EntryFailure.None, plot, null, now);
    }
}
