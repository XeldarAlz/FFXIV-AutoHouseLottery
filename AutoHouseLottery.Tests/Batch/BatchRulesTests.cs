using AutoHouseLottery.Core.Batch.Model;
using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Selection.Model;
using AutoHouseLottery.Core.Tasks.Model;
using static AutoHouseLottery.Tests.Batch.BatchPlanningTests;
using static AutoHouseLottery.Tests.CharacterFixtures;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests.Batch;

public sealed class BatchRulesTests
{
    private static readonly DateTime now = CycleStart.AddHours(12);

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
        batch = BatchRules.Skip(batch, batch.IndexOf(2), EligibilityReason.InsufficientGil, EntryFailure.OverBudget, now);

        var report = BatchRules.Report(batch);

        Assert.Equal(new BatchReport(1, 0, 1, 1, 0, 1, 0, Deposit), report);
        Assert.Equal(4, report.Total);
        Assert.True(BatchRules.DisplayRank(BatchMemberState.Entered) < BatchRules.DisplayRank(BatchMemberState.NotEntered));
        Assert.True(BatchRules.DisplayRank(BatchMemberState.Entered) < BatchRules.DisplayRank(BatchMemberState.Excluded));
    }

    [Fact]
    public void EachMemberKeepsItsOwnCapAndReserveSoNothingIsPooled()
    {
        var keys = Ward(2);
        var roster = new[] { Character(1), Character(2) };
        var policy = Policy(keys, Settings(1) with { DepositCap = 4_000_000, GilReserve = 10 }, Settings(2) with { DepositCap = 3_500_000, GilReserve = 20 });

        var batch = BatchRules.Create(Guid.NewGuid(), policy, AssignmentPlanner.Build(policy, roster, Listed(keys), now), roster, true, now);

        Assert.Equal(policy.Characters[0], batch.Members[batch.IndexOf(1)].Settings);
        Assert.Equal(policy.Characters[1], batch.Members[batch.IndexOf(2)].Settings);
        Assert.Equal(Deposit, batch.Members[0].Assignment!.Value.MaximumDeposit);
        Assert.Equal(Deposit, batch.Members[1].Assignment!.Value.MaximumDeposit);
    }

    // Three ungrouped private characters on one world: one ownership group, Same plot, in policy order.
    internal static BatchCheckpoint Batch(out RunPolicy policy)
    {
        var keys = Ward(5);
        var roster = new[] { Character(1), Character(2), Character(3) };
        policy = Policy(keys, Settings(1), Settings(2), Settings(3));
        return BatchRules.Create(Guid.NewGuid(), policy, AssignmentPlanner.Build(policy, roster, Listed(keys), now), roster, dryRunRequested: true, now);
    }

    private static BatchCheckpoint Done(BatchCheckpoint batch, ulong contentId, BatchMemberState state)
        => BatchRules.Record(batch, batch.IndexOf(contentId), state, state == BatchMemberState.NotEntered ? EntryFailure.Sold : EntryFailure.None, null, null, now);
}
