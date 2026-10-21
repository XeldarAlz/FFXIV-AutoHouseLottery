using System.IO;
using AutoHouseLottery.Core.Batch.Model;
using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.External;
using AutoHouseLottery.Core.Game;
using AutoHouseLottery.Core.Game.Character;
using AutoHouseLottery.Core.Game.Watchers;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence;
using AutoHouseLottery.Core.Selection.Model;
using AutoHouseLottery.Core.Tasks.Model;
using AutoHouseLottery.Core.Travel;
using AutoHouseLottery.Core.Travel.Model;
using ECommons.DalamudServices;

namespace AutoHouseLottery.Core.Tasks;

// A batch (Version 1.1): one entry per selected character, each through the same entry run as a single run, one character
// after another. Between characters it either switches through Lifestream, when Lifestream offers its character change,
// after its checkpoint is on disk; or it waits, without the lease, for the next member's own login and the player's
// Continue. Only
// the logged-in character is ever acted for, and only after its content ID and home world match the member, with its
// gil, Free Company and estates read again; its run then reads its lottery record before anything else.
internal sealed partial class RunController
{
    private readonly BatchCheckpointFile batchFile;
    private BatchCheckpoint? batch;
    private bool batchStepDue;
    private BatchEnd memberEnd;
    private (BatchHalt Halt, string Reason)? deferredHalt;
    private long batchSwitchToken;
    private int switchMember = -1;
    private System.Threading.Tasks.Task<bool>? switchCheckpoint;
    private bool switchCapable;

    public BatchCheckpoint? Batch => batch;

    public bool BatchActive => batch is { Active: true };

    public bool BatchSwitchingAllowed => switchCapable;

    // What StartBatch would say now, without starting anything: Find a home shows it under Enter for N characters.
    public StartRefusal CheckBatch(RunPolicy policy) => PlanBatch(policy).Refusal;

    public StartRefusal StartBatch(RunPolicy policy)
    {
        var plan = PlanBatch(policy);
        if (plan is not { Refusal: StartRefusal.None, Preview: { } preview })
        {
            RunLog.Info($"Batch not started: {plan.Refusal}{(plan.Preview is { } shown ? $", preview blockers {shown.Blockers}" : string.Empty)}.");
            return plan.Refusal;
        }

        if (!lease.TryAcquire(LeaseWork.Entry, out var leaseGrant))
        {
            return StartRefusal.Busy;
        }

        owner = leaseGrant.Owner;
        var now = DateTime.UtcNow;
        batch = BatchRules.Create(Guid.NewGuid(), policy, preview, plan.Roster, now);
        finished = RunStatus.Idle;
        PauseReason = PauseReason.None;
        RunLog.Info($"Batch {batch.BatchId} authorized: {preview.Assignments.Length} entr(ies) for {policy.Characters.Length} selected character(s), policy revision {policy.Revision}, "
                  + $"cycle {policy.Cycle.Cycle} ({policy.Cycle.Evidence}), placement {policy.Placement}, "
                  + $"switching {(BatchSwitchingAllowed ? "through Lifestream" : "not available, so each character waits for its own login")}.");
        for (var index = 0; index < batch.Members.Length; index++)
        {
            RunLog.Info($"Batch {batch.BatchId}: {DescribeMember(batch.Members[index])}.");
        }

        SaveBatch();
        batchStepDue = true;
        return StartRefusal.None;
    }

    // Whether Continue would do something now, and why not, with the same checks Continue makes. Cheap enough for a frame.
    public StartRefusal ContinueCheck()
    {
        if (batch is not { Phase: BatchPhase.WaitingForLogin or BatchPhase.Halted } waiting)
        {
            return StartRefusal.NoAssignment;
        }

        if (Busy)
        {
            return StartRefusal.AlreadyRunning;
        }

        if (TaskStarter.Busy || lease.Owner is not null)
        {
            return StartRefusal.Busy;
        }

        if (!Svc.ClientState.IsLoggedIn || !Svc.PlayerState.IsLoaded)
        {
            return StartRefusal.NotLoggedIn;
        }

        var region = WorldRegionReader.Of(Svc.PlayerState.HomeWorld.RowId);
        if (!WorldRegions.Supported(region))
        {
            return region == WorldRegion.Unknown ? StartRefusal.UnknownRegion : StartRefusal.UnsupportedRegion;
        }

        if (AutoAnswerGuard.Blocks)
        {
            return StartRefusal.AutoAnswerPlugin;
        }

        if (DutyWatcher.InContent())
        {
            return StartRefusal.InContent;
        }

        if (!housing.JournalWritable)
        {
            return StartRefusal.JournalUnavailable;
        }

        var reevaluated = BatchRules.Reevaluate(waiting, housing.JournalSnapshot, DateTime.UtcNow);
        return BatchRules.ContinueRefusal(BatchRules.Next(reevaluated, Svc.PlayerState.ContentId, BatchSwitchingAllowed).Kind);
    }

    // The player's Continue for a batch that waits for a login or was halted. Nothing restarts on its own after a halt or a
    // reload; this is the explicit restart. Members whose entry was in question are looked at again from the journal.
    public StartRefusal ContinueBatch()
    {
        if (batch is not { Phase: BatchPhase.WaitingForLogin or BatchPhase.Halted } waiting)
        {
            return StartRefusal.NoAssignment;
        }

        var now = DateTime.UtcNow;
        if (BatchRules.Expired(waiting, housing.CurrentPosition(now), now))
        {
            ExpireBatch(waiting, now);
            return StartRefusal.EntryPeriodClosed;
        }

        var refusal = ContinueCheck();
        if (refusal != StartRefusal.None || !lease.TryAcquire(LeaseWork.Entry, out var leaseGrant))
        {
            refusal = refusal == StartRefusal.None ? StartRefusal.Busy : refusal;
            RunLog.Info($"Batch {waiting.BatchId}: not continued ({refusal}).");
            return refusal;
        }

        owner = leaseGrant.Owner;
        batch = BatchRules.Resume(BatchRules.Reevaluate(waiting, housing.JournalSnapshot, now), now);
        finished = RunStatus.Idle;
        RunLog.Info($"Batch {waiting.BatchId}: continued by the player from {waiting.Phase}{(waiting.Halt == BatchHalt.None ? string.Empty : $" ({waiting.Halt})")}; "
                  + $"{DescribeReport(BatchRules.Report(batch))}.");
        SaveBatch();
        batchStepDue = true;
        return StartRefusal.None;
    }

    // Drops a batch that is not running, with what it still had queued. Its entries stay in the journal and My lottery.
    public void DismissBatch()
    {
        if (batch is not { Active: false } done)
        {
            return;
        }

        RunLog.Info($"Batch {done.BatchId}: dismissed at {done.Phase}; {DescribeReport(BatchRules.Report(done))}.");
        batch = null;
        SaveBatch();
    }

    private BatchPlan PlanBatch(RunPolicy policy)
    {
        var context = CharacterContextReader.Read();
        var policyError = RunPolicyRules.Validate(policy);
        var roster = policyError == RunPolicyError.None ? housing.Roster(ContentIds(policy), context, policy.Cycle) : [];
        var preview = policyError == RunPolicyError.None ? AssignmentPlanner.Build(policy, roster, housing.Plots, DateTime.UtcNow) : null;
        var loggedInAssigned = context is not null && preview is not null && FindAssignment(preview.Assignments, context.ContentId) is not null;
        var facts = new BatchStartFacts(
            Busy,
            TaskStarter.Busy || lease.Owner is not null,
            batch is { Phase: not BatchPhase.Finished },
            context is not null,
            context is null ? WorldRegion.Unknown : WorldRegionReader.Of(context.HomeWorldId),
            AutoAnswerGuard.Blocks,
            DutyWatcher.InContent(),
            policy.Shortlist is not null && policy.Exact is null,
            policyError,
            preview?.Blockers ?? RunBlocker.None,
            housing.JournalWritable,
            preview?.Assignments.Length ?? 0,
            loggedInAssigned,
            loggedInAssigned && housing.HasPendingAttempt(context!.ContentId, policy.Cycle.Cycle),
            BatchSwitchingAllowed);
        return new BatchPlan(BatchStartRules.Check(facts), preview, roster);
    }

    // Once a second, logged in or not: a pending switch checkpoint, then the switch the batch asked for.
    private void TickBatchSwitch()
    {
        if (switchCheckpoint is { } saving)
        {
            if (!saving.IsCompleted)
            {
                return;
            }

            switchCheckpoint = null;
            if (!saving.Result)
            {
                HaltBatch(BatchHalt.CheckpointNotSaved, "the checkpoint could not be saved before the switch");
                return;
            }

            SendSwitch(switchMember);
            return;
        }

        if (batchSwitchToken != 0)
        {
            ObserveBatchSwitch();
        }
    }

    // Once a second while logged in: a batch whose entry period is over is finished, and a running one takes its next
    // step once nothing else is going.
    private void TickBatch()
    {
        if (batch is not { Phase: not (BatchPhase.Finished or BatchPhase.Switching) } running || session is not null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (BatchRules.Expired(running, housing.CurrentPosition(now), now))
        {
            ExpireBatch(running, now);
            return;
        }

        if (running.Phase != BatchPhase.Running || !batchStepDue || pendingJournalWrite is { IsCompleted: false } || closeConfirmationUntilMs != 0 || DutyWatcher.InContent())
        {
            return;
        }

        if (AutoAnswerGuard.Blocks)
        {
            HaltBatch(BatchHalt.AutoAnswerPlugin, "a plugin that answers dialogs on its own is loaded");
            return;
        }

        batchStepDue = false;
        var step = BatchRules.Next(running, Svc.PlayerState.ContentId, BatchSwitchingAllowed);
        switch (step.Kind)
        {
            case BatchStepKind.Process:
                StartMember(running, step.MemberIndex, now);
                return;
            case BatchStepKind.Switch:
                PrepareSwitch(running, step.MemberIndex, now);
                return;
            case BatchStepKind.WaitForLogin:
                batch = BatchRules.Wait(running, now);
                ReleaseBatchLease();
                RunLog.Info($"Batch {running.BatchId}: switching is not available, so {running.Members[step.MemberIndex].Name} and the others still queued wait for their own login and a Continue; "
                          + $"{DescribeReport(BatchRules.Report(batch))}.");
                SaveBatch();
                return;
            case BatchStepKind.Held:
                HaltBatch(BatchHalt.NeedsReconciliation, $"{running.Members[step.MemberIndex].Name} and the others still queued wait until their ownership group's entry in question is checked");
                return;
            default:
                batch = BatchRules.Finish(running, BatchHalt.None, now);
                ReleaseBatchLease();
                RunLog.Info($"Batch {running.BatchId}: finished; {DescribeReport(BatchRules.Report(batch))}.");
                SaveBatch();
                SendPostRunCommands(BatchRules.FinishedOnItsOwn(batch), $"batch {running.BatchId} has an entry in question");
                return;
        }
    }

    // The member is the logged-in character. Its identity is matched and its state read again before its run starts; the
    // previous character's profile, gil or reading is never used for it. A member the fresh check rules out is reported
    // and the batch goes on.
    private void StartMember(BatchCheckpoint running, int index, DateTime now)
    {
        var member = running.Members[index];
        var context = CharacterContextReader.Read();
        if (context is null || context.ContentId != member.ContentId || context.HomeWorldId != member.HomeWorldId)
        {
            HaltBatch(BatchHalt.CharacterChanged, $"{member.Name} is not the logged-in character");
            return;
        }

        var cycle = running.Policy.Cycle.Cycle;
        if (housing.HasPendingAttempt(member.ContentId, cycle))
        {
            batch = BatchRules.Record(running, index, BatchMemberState.Uncertain, EntryFailure.SubmissionUncertain, null, null, now);
            HaltBatch(BatchHalt.NeedsReconciliation, $"{member.Name} has an entry attempt in question");
            return;
        }

        var assignment = member.Assignment!.Value;
        var profile = housing.Profile(context, housing.LastReading(member.ContentId), running.Policy.Cycle);
        var characterCheck = EligibilityRules.EvaluateCharacter(profile, member.Settings.Buyer, cycle, AutomaticRefunds(member.ContentId));
        var budget = new CharacterBudget(member.Settings.DepositCap, member.Settings.GilReserve, running.Policy.TravelAllowance);
        var funds = BatchRules.Affordable(budget, assignment.TargetOrder, running.Ranking, context.Gil);
        var (reasons, failure) = BatchRules.Recheck(characterCheck.Blockers, funds);
        if (reasons != EligibilityReason.None)
        {
            batch = BatchRules.Skip(running, index, reasons, failure, profile.Participation.EnteredPlot, now);
            RunLog.Info($"Batch {running.BatchId}: {member.Name} is not entered after a fresh check ({reasons}, funds {funds}; gil {context.Gil?.ToString() ?? "unknown"}).");
            SaveBatch();
            batchStepDue = true;
            return;
        }

        var (number, count) = Position(running);
        var mode = BatchRules.ModeOf(assignment);
        var active = new EntryRunSession(Guid.NewGuid(), mode, running.Policy, assignment, member.Settings, context.Name, running.Ranking, number, count);
        batch = BatchRules.Begin(running, index, now);
        session = active;
        memberEnd = BatchEnd.Finished;
        deferredHalt = null;
        finished = RunStatus.Idle;
        PauseReason = PauseReason.None;
        RunLog.Info($"Batch {running.BatchId}: character {number} of {count}, {context.Name}, run {active.RunId} ({mode}), gil {context.Gil?.ToString() ?? "unknown"}, "
                  + $"targets {Describe(assignment.TargetOrder)}, up to {assignment.MaximumDeposit} gil.");
        SaveBatch();
        Launch(active);
    }

    // Called from Settle when a member's run has ended, on the framework thread. A halt asked for while the run was still
    // going (a GM alert while a press was being verified) is applied here, after the member's own outcome is recorded.
    private void MemberEnded(BatchCheckpoint running, int index, EntryRunSession ended)
    {
        var now = DateTime.UtcNow;
        var (state, halt) = BatchRules.Classify(ended.Step, ended.Failure, memberEnd);
        PlotKey? plot = null;
        long? deposit = null;
        if (state == BatchMemberState.Entered)
        {
            plot = ended.Target;
            deposit = ended.Ledger.GrossSubmitted;
        }
        else if (state == BatchMemberState.NotEntered && ended.Failure == EntryFailure.AlreadyEntered)
        {
            plot = EnteredPlotOf(running.Members[index], running.Policy.Cycle);
        }

        var deferred = deferredHalt;
        memberEnd = BatchEnd.Finished;
        deferredHalt = null;
        batch = BatchRules.Record(running, index, state, ended.Failure, plot, deposit, now);
        RunLog.Info($"Batch {running.BatchId}: {DescribeMember(batch.Members[index])}.");
        if (halt != BatchHalt.None || deferred is not null)
        {
            HaltBatch(halt != BatchHalt.None ? halt : deferred!.Value.Halt, halt != BatchHalt.None ? ended.Step.ToString() : deferred!.Value.Reason);
            return;
        }

        SaveBatch();
        batchStepDue = true;
    }

    // Before a switch every pending action is settled: an entry in question halts the batch, nothing else may be using the
    // game, and the checkpoint must be on disk before Lifestream is asked for anything.
    private void PrepareSwitch(BatchCheckpoint running, int index, DateTime now)
    {
        var current = Svc.PlayerState.ContentId;
        if (housing.HasPendingAttempt(current, running.Policy.Cycle.Cycle))
        {
            HaltBatch(BatchHalt.NeedsReconciliation, "the logged-in character has an entry attempt in question");
            return;
        }

        if (CharacterSwitch.Blocked() is var blocked and not SwitchBlock.None)
        {
            HaltBatch(BatchHalt.SwitchBlocked, $"{blocked} is using the game");
            return;
        }

        var member = running.Members[index];
        batch = BatchRules.BeginSwitch(running, member.ContentId, now);
        switchMember = index;
        switchCheckpoint = batchFile.Save(batch);
        RunLog.Info($"Batch {running.BatchId}: saving the checkpoint before switching to {member.Name}.");
    }

    private void SendSwitch(int index)
    {
        if (batch is not { Phase: BatchPhase.Switching } switching)
        {
            return;
        }

        var member = switching.Members[index];
        if (WorldNames.Of(member.HomeWorldId) is not { } worldName)
        {
            HaltBatch(BatchHalt.SwitchFailed, $"the home world {member.HomeWorldId} of {member.Name} has no name");
            return;
        }

        var start = CharacterSwitch.Begin(member.ContentId, member.Name, member.HomeWorldId, worldName);
        if (start.Result != SwitchStartResult.Started)
        {
            HaltBatch(BatchRules.SwitchNotStarted(start.Result), $"the switch to {member.Name} did not start ({start.Result})");
            return;
        }

        batchSwitchToken = start.Token;
        RunLog.Info($"Batch {switching.BatchId}: switching to {member.Name} of {worldName} (switch {start.Token}); nothing acts until that character is confirmed.");
    }

    private void ObserveBatchSwitch()
    {
        var state = CharacterSwitch.Observe(batchSwitchToken);
        if (state == SwitchState.Switching || batch is not { Phase: BatchPhase.Switching } switching)
        {
            return;
        }

        batchSwitchToken = 0;
        if (state != SwitchState.Switched)
        {
            HaltBatch(state is SwitchState.WrongCharacter or SwitchState.WrongHomeWorld ? BatchHalt.WrongCharacter : BatchHalt.SwitchFailed, $"the switch ended {state}");
            return;
        }

        var context = CharacterContextReader.Read();
        var verdict = context is null ? LoginVerdict.WrongCharacter : BatchRules.OnLogin(switching, context.ContentId, context.HomeWorldId);
        if (verdict != LoginVerdict.RequestedCharacter)
        {
            HaltBatch(BatchHalt.WrongCharacter, $"the logged-in character does not match ({verdict})");
            return;
        }

        batch = BatchRules.Resume(switching, DateTime.UtcNow);
        RunLog.Info($"Batch {switching.BatchId}: {context!.Name} of world {context.HomeWorldId} is confirmed by content ID; its state is read again before its run.");
        SaveBatch();
        batchStepDue = true;
    }

    // While a member's run is still going, the halt waits for MemberEnded, so the member's own outcome is recorded first.
    private void HaltBatch(BatchHalt halt, string reason)
    {
        if (batch is not { } current)
        {
            return;
        }

        if (session is not null)
        {
            deferredHalt ??= (halt, reason);
            RunLog.Info($"Batch {current.BatchId}: halt ({halt}, {reason}) waits for the running member's outcome.");
            return;
        }

        if (batchSwitchToken != 0)
        {
            CharacterSwitch.CancelOwned(batchSwitchToken);
            batchSwitchToken = 0;
        }

        switchCheckpoint = null;
        batchStepDue = false;
        batch = BatchRules.Halt(current, halt, DateTime.UtcNow);
        ReleaseBatchLease();
        RunLog.Info($"Batch {current.BatchId}: halted ({halt}, {reason}); {DescribeReport(BatchRules.Report(batch))}. It continues only from the player's Continue.");
        SaveBatch();
    }

    private void ExpireBatch(BatchCheckpoint current, DateTime now)
    {
        batch = BatchRules.Expire(current, now);
        batchStepDue = false;
        ReleaseBatchLease();
        RunLog.Info($"Batch {current.BatchId}: its entry period is over; {DescribeReport(BatchRules.Report(batch))}.");
        SaveBatch();
    }

    // A logout the batch did not ask for, or a GM alert, halts it between members; its own switch logs out by design.
    private void InterruptBatch(string reason)
    {
        if (batch is { Phase: BatchPhase.Running } && session is null)
        {
            HaltBatch(BatchHalt.Interrupted, reason);
        }
    }

    // A waiting member logged in on its own: the player is told once that the batch can go on for it.
    private void AnnounceQueuedLogin(ulong contentId, uint homeWorldId)
    {
        if (batch is not { Phase: BatchPhase.WaitingForLogin or BatchPhase.Halted } waiting || BatchRules.OnLogin(waiting, contentId, homeWorldId) != LoginVerdict.QueuedCharacter)
        {
            return;
        }

        var member = waiting.Members[waiting.IndexOf(contentId)];
        RunLog.Info($"Batch {waiting.BatchId}: {member.Name} logged in and is waiting; Continue goes on for it.");
        Svc.Chat.Print($"{HouseLotteryConstants.LogPrefix} {Localization.Loc.T(Localization.L.Batch.LoginNotice, member.Name)}");
    }

    private void ReleaseBatchLease()
    {
        if (session is not null || owner is not { } holder)
        {
            return;
        }

        lease.Release(holder);
        owner = null;
    }

    private void SaveBatch() => batchFile.Save(batch);

    // Read back at load. A batch that was running or switching lost its run and its switch with the reload, so it waits
    // for the player's Continue.
    private BatchCheckpoint? RestoreBatch()
    {
        if (batchFile.Load(DateTime.UtcNow) is not { } stored)
        {
            return null;
        }

        var restored = BatchRules.Restore(stored, DateTime.UtcNow);
        RunLog.Info($"Batch {restored.BatchId}: read back at {stored.Phase}, now {restored.Phase}{(restored.Halt == BatchHalt.None ? string.Empty : $" ({restored.Halt})")}; "
                  + $"{DescribeReport(BatchRules.Report(restored))}.");
        if (!ReferenceEquals(restored, stored))
        {
            batchFile.Save(restored);
        }

        return restored;
    }

    // The batch member whose run just ended, or -1 for a single run.
    private int RunningMember(EntryRunSession ended)
    {
        if (batch is not { } current)
        {
            return -1;
        }

        var index = current.IndexOf(ended.Settings.ContentId);
        return index >= 0 && current.Members[index].State == BatchMemberState.Running ? index : -1;
    }

    // The plot a member's own run found it already entered on, from the journal and the run's lottery reading; null when
    // neither can tell, which leaves the member's group out of the batch rather than guessing.
    private PlotKey? EnteredPlotOf(BatchMember member, in CyclePosition cycle)
    {
        var slot = housing.Participation(member.ContentId, member.HomeWorldId, housing.LastReading(member.ContentId), cycle);
        return slot.StateIn(cycle.Cycle) is EntrySlotState.Entered or EntrySlotState.Unresolved ? slot.EnteredPlot : null;
    }

    private static (int Number, int Count) Position(BatchCheckpoint running)
    {
        var count = 0;
        var done = 0;
        for (var index = 0; index < running.Members.Length; index++)
        {
            var member = running.Members[index];
            if (member.Assignment is null)
            {
                continue;
            }

            count++;
            if (!member.Waiting)
            {
                done++;
            }
        }

        return (done + 1, count);
    }

    private static ulong[] ContentIds(RunPolicy policy)
    {
        var ids = new ulong[policy.Characters.Length];
        for (var index = 0; index < ids.Length; index++)
        {
            ids[index] = policy.Characters[index].ContentId;
        }

        return ids;
    }

    private static string DescribeMember(BatchMember member)
        => $"{member.Name} ({member.ContentId:X}) {member.State}"
         + (member.Assignment is { } assignment ? $", {assignment.Placement}{(assignment.Exact ? " exact" : string.Empty)} on {Describe(assignment.TargetOrder)}, up to {assignment.MaximumDeposit} gil" : string.Empty)
         + (member.Reasons == EligibilityReason.None ? string.Empty : $", reasons {member.Reasons}")
         + (member.Failure == EntryFailure.None ? string.Empty : $", {member.Failure}")
         + (member.Plot is { } plot ? $", {plot} for {member.Deposit?.ToString() ?? "unknown"} gil" : string.Empty);

    private static string DescribeReport(in BatchReport report)
        => $"{report.Entered} entered ({report.Deposited} gil), {report.Uncertain} in question, {report.Queued} waiting, "
         + $"{report.NotEntered} not entered, {report.Excluded} excluded";

    private static BatchCheckpointFile CreateBatchFile(string directory)
        => new(Path.Combine(directory, BatchCheckpointFile.FileName), static (exception, message) => RunLog.Warning(exception, message));

    private readonly record struct BatchPlan(StartRefusal Refusal, RunPreview? Preview, CharacterProfile[] Roster);
}
