using System.Collections.Immutable;
using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Selection.Model;
using AutoHouseLottery.Core.Shared;
using AutoHouseLottery.Core.Tasks.Model;

namespace AutoHouseLottery.Core.Batch.Model;

internal enum BatchStepKind : byte { Process, Switch, WaitForLogin, Finished }

internal readonly record struct BatchStep(BatchStepKind Kind, int MemberIndex);

// RequestedCharacter: the character the batch switched to. QueuedCharacter: a member still waiting logged in on its own.
// WrongCharacter: somebody else logged in while the batch waited for a switch. WrongHomeWorld: the right content ID with
// another home world than the batch recorded.
internal enum LoginVerdict : byte { NotInBatch, RequestedCharacter, QueuedCharacter, WrongCharacter, WrongHomeWorld }

internal readonly record struct BatchReport(int Entered, int DryRun, int NotEntered, int Excluded, int Uncertain, int Queued, int Running, long Deposited)
{
    public int Total => Entered + DryRun + NotEntered + Excluded + Uncertain + Queued + Running;
}

// The batch of TECHNICAL_DESIGN sections 10 and 14 as plain data: each selected character makes at most one entry, one
// after another, through the same entry run as a single character; nothing is pooled between them, and every step that
// changes the batch returns a new checkpoint.
internal static class BatchRules
{
    // The preview's assignments in their planned order, exact targets first, then the characters the plan left out.
    public static BatchCheckpoint Create(Guid batchId, RunPolicy policy, RunPreview preview, ReadOnlySpan<CharacterProfile> roster, bool dryRunRequested, DateTime nowUtc)
    {
        var members = ImmutableArray.CreateBuilder<BatchMember>(preview.Assignments.Length + preview.Exclusions.Length);
        for (var index = 0; index < preview.Assignments.Length; index++)
        {
            var assignment = preview.Assignments[index];
            members.Add(Member(policy, roster, assignment.ContentId, nowUtc) with { Assignment = assignment, State = BatchMemberState.Queued });
        }

        for (var index = 0; index < preview.Exclusions.Length; index++)
        {
            var exclusion = preview.Exclusions[index];
            members.Add(Member(policy, roster, exclusion.ContentId, nowUtc) with { State = BatchMemberState.Excluded, Reasons = exclusion.Reasons });
        }

        return new BatchCheckpoint
        {
            BatchId = batchId,
            Policy = policy,
            Ranking = preview.Ranking,
            DryRunRequested = dryRunRequested,
            Members = members.MoveToImmutable(),
            Phase = BatchPhase.Running,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
        };
    }

    // The logged-in character goes first when it is still waiting, so a switch is never spent on it. Otherwise the next
    // waiting member is switched to when switching is allowed, or waits for its own login when it is not.
    public static BatchStep Next(BatchCheckpoint checkpoint, ulong? loggedIn, bool switchingAllowed)
    {
        var firstWaiting = -1;
        for (var index = 0; index < checkpoint.Members.Length; index++)
        {
            var member = checkpoint.Members[index];
            if (!member.Waiting)
            {
                continue;
            }

            if (member.ContentId == loggedIn)
            {
                return new BatchStep(BatchStepKind.Process, index);
            }

            if (firstWaiting < 0)
            {
                firstWaiting = index;
            }
        }

        if (firstWaiting < 0)
        {
            return new BatchStep(BatchStepKind.Finished, -1);
        }

        return switchingAllowed ? new BatchStep(BatchStepKind.Switch, firstWaiting) : new BatchStep(BatchStepKind.WaitForLogin, firstWaiting);
    }

    public static BatchCheckpoint Begin(BatchCheckpoint checkpoint, int index, DateTime nowUtc)
        => checkpoint with
        {
            Members = checkpoint.Members.SetItem(index, checkpoint.Members[index] with { State = BatchMemberState.Running, Failure = EntryFailure.None, UpdatedUtc = nowUtc }),
            Phase = BatchPhase.Running,
            Halt = BatchHalt.None,
            SwitchTarget = null,
            UpdatedUtc = nowUtc,
        };

    // A member a fresh check rules out before its run starts: its slot, an earlier entry still to settle, its gil.
    public static BatchCheckpoint Skip(BatchCheckpoint checkpoint, int index, EligibilityReason reasons, EntryFailure failure, DateTime nowUtc)
        => checkpoint with
        {
            Members = checkpoint.Members.SetItem(index, checkpoint.Members[index] with { State = BatchMemberState.NotEntered, Reasons = reasons, Failure = failure, UpdatedUtc = nowUtc }),
            UpdatedUtc = nowUtc,
        };

    // How a member's run ended, for the batch. A run that ended before anything was entered leaves the member waiting
    // when the player stopped it or something cut it short, so a resume starts it again from a fresh check. A press that
    // may have reached the game halts the batch: no switch happens while an entry is in question.
    public static (BatchMemberState State, BatchHalt Halt) Classify(EntryStep step, EntryFailure failure, BatchEnd end, bool dryRunTraced)
    {
        var halt = end switch
        {
            BatchEnd.StoppedByPlayer => BatchHalt.StoppedByPlayer,
            BatchEnd.Interrupted => BatchHalt.Interrupted,
            _ => BatchHalt.None,
        };
        switch (step)
        {
            case EntryStep.Completed:
                return (dryRunTraced ? BatchMemberState.DryRun : BatchMemberState.Entered, halt);
            case EntryStep.CompletedPartial:
                return (BatchMemberState.NotEntered, halt);
            case EntryStep.NeedsReconciliation:
                return (BatchMemberState.Uncertain, BatchHalt.NeedsReconciliation);
        }

        if (end != BatchEnd.Finished)
        {
            return (BatchMemberState.Queued, halt);
        }

        return failure switch
        {
            EntryFailure.None => (BatchMemberState.Queued, BatchHalt.Interrupted),
            EntryFailure.CharacterChanged => (BatchMemberState.Queued, BatchHalt.CharacterChanged),
            EntryFailure.JournalUnavailable => (BatchMemberState.Queued, BatchHalt.JournalUnavailable),
            _ => (BatchMemberState.NotEntered, BatchHalt.None),
        };
    }

    // Records a member's outcome. An entry, or a dry run that reached its confirmation, on a Same plot assignment fixes
    // that plot for the members of its group still waiting, as a real entry would (AssignmentPlanner.FollowConfirmedEntry).
    public static BatchCheckpoint Record(BatchCheckpoint checkpoint, int index, BatchMemberState state, EntryFailure failure, PlotKey? plot, long? deposit, DateTime nowUtc)
    {
        var member = checkpoint.Members[index] with
        {
            State = state,
            Failure = state is BatchMemberState.Entered or BatchMemberState.DryRun ? EntryFailure.None : failure,
            Plot = plot,
            Deposit = deposit,
            UpdatedUtc = nowUtc,
        };
        var members = checkpoint.Members.SetItem(index, member);
        if (state is BatchMemberState.Entered or BatchMemberState.DryRun && plot is { } entered && member.Assignment is { Placement: PlacementMode.SamePlot } lead && lead.TargetOrder.IndexOf(entered) >= 0)
        {
            members = Follow(members, lead.ContentId, entered);
        }

        return checkpoint with { Members = members, UpdatedUtc = nowUtc };
    }

    public static BatchCheckpoint Halt(BatchCheckpoint checkpoint, BatchHalt halt, DateTime nowUtc)
        => checkpoint with { Members = Requeue(checkpoint.Members, nowUtc), Phase = BatchPhase.Halted, Halt = halt, SwitchTarget = null, UpdatedUtc = nowUtc };

    public static BatchCheckpoint Wait(BatchCheckpoint checkpoint, DateTime nowUtc)
        => checkpoint with { Phase = BatchPhase.WaitingForLogin, Halt = BatchHalt.None, SwitchTarget = null, UpdatedUtc = nowUtc };

    public static BatchCheckpoint Resume(BatchCheckpoint checkpoint, DateTime nowUtc)
        => checkpoint with { Phase = BatchPhase.Running, Halt = BatchHalt.None, SwitchTarget = null, UpdatedUtc = nowUtc };

    public static BatchCheckpoint BeginSwitch(BatchCheckpoint checkpoint, ulong contentId, DateTime nowUtc)
        => checkpoint with { Phase = BatchPhase.Switching, Halt = BatchHalt.None, SwitchTarget = contentId, UpdatedUtc = nowUtc };

    public static BatchCheckpoint Finish(BatchCheckpoint checkpoint, BatchHalt halt, DateTime nowUtc)
        => checkpoint with { Members = Requeue(checkpoint.Members, nowUtc), Phase = BatchPhase.Finished, Halt = halt, SwitchTarget = null, UpdatedUtc = nowUtc };

    // Read back after the plugin loaded: a batch that was running or switching lost its run and its switch with the
    // reload, so it waits for the player's explicit Continue instead of acting on its own.
    public static BatchCheckpoint Restore(BatchCheckpoint checkpoint, DateTime nowUtc)
        => checkpoint.Active ? Halt(checkpoint, BatchHalt.Reloaded, nowUtc) : checkpoint;

    // A batch binds to its cycle's entry period. Once that is over, every member still waiting is reported as not entered
    // and the batch is finished.
    public static bool Expired(BatchCheckpoint checkpoint, in CyclePosition current, DateTime nowUtc)
        => current.Cycle != checkpoint.Policy.Cycle.Cycle || nowUtc >= checkpoint.Policy.Cycle.PhaseEndUtc;

    public static BatchCheckpoint Expire(BatchCheckpoint checkpoint, DateTime nowUtc)
    {
        var members = checkpoint.Members.ToBuilder();
        for (var index = 0; index < members.Count; index++)
        {
            if (members[index].State is BatchMemberState.Queued or BatchMemberState.Running)
            {
                members[index] = members[index] with { State = BatchMemberState.NotEntered, Failure = EntryFailure.CycleChanged, UpdatedUtc = nowUtc };
            }
        }

        return checkpoint with { Members = members.MoveToImmutable(), Phase = BatchPhase.Finished, Halt = BatchHalt.CycleEnded, SwitchTarget = null, UpdatedUtc = nowUtc };
    }

    // The character the game just logged in, against what the batch expects. While a switch is pending only its target
    // counts, and only on the home world the batch recorded for it.
    public static LoginVerdict OnLogin(BatchCheckpoint checkpoint, ulong contentId, uint homeWorldId)
    {
        if (checkpoint.SwitchTarget is { } target)
        {
            if (contentId != target)
            {
                return LoginVerdict.WrongCharacter;
            }

            var requested = checkpoint.IndexOf(target);
            return requested >= 0 && checkpoint.Members[requested].HomeWorldId == homeWorldId ? LoginVerdict.RequestedCharacter : LoginVerdict.WrongHomeWorld;
        }

        var index = checkpoint.IndexOf(contentId);
        if (index < 0 || !checkpoint.Members[index].Waiting)
        {
            return LoginVerdict.NotInBatch;
        }

        return checkpoint.Members[index].HomeWorldId == homeWorldId ? LoginVerdict.QueuedCharacter : LoginVerdict.WrongHomeWorld;
    }

    // Counts for the summary. Every outcome is counted on its own, so characters that were excluded, poor or not logged in
    // never hide the entries the others made.
    public static BatchReport Report(BatchCheckpoint checkpoint)
    {
        int entered = 0, dryRun = 0, notEntered = 0, excluded = 0, uncertain = 0, queued = 0, running = 0;
        var deposited = 0L;
        for (var index = 0; index < checkpoint.Members.Length; index++)
        {
            var member = checkpoint.Members[index];
            switch (member.State)
            {
                case BatchMemberState.Entered:
                    entered++;
                    deposited = Gil.SaturatingAdd(deposited, member.Deposit ?? 0);
                    break;
                case BatchMemberState.DryRun:
                    dryRun++;
                    break;
                case BatchMemberState.NotEntered:
                    notEntered++;
                    break;
                case BatchMemberState.Excluded:
                    excluded++;
                    break;
                case BatchMemberState.Uncertain:
                    uncertain++;
                    break;
                case BatchMemberState.Running:
                    running++;
                    break;
                default:
                    queued++;
                    break;
            }
        }

        return new BatchReport(entered, dryRun, notEntered, excluded, uncertain, queued, running, deposited);
    }

    // The order the summary lists members in: what was entered first, then what is in question, then what still waits,
    // then what did not enter.
    public static int DisplayRank(BatchMemberState state) => state switch
    {
        BatchMemberState.Entered => 0,
        BatchMemberState.DryRun => 1,
        BatchMemberState.Running => 2,
        BatchMemberState.Uncertain => 3,
        BatchMemberState.Queued => 4,
        BatchMemberState.NotEntered => 5,
        _ => 6,
    };

    private static ImmutableArray<BatchMember> Follow(ImmutableArray<BatchMember> members, ulong leadContentId, PlotKey plot)
    {
        var assignments = ImmutableArray.CreateBuilder<RunAssignment>();
        for (var index = 0; index < members.Length; index++)
        {
            if (members[index].Assignment is { } assignment)
            {
                assignments.Add(assignment);
            }
        }

        var followed = AssignmentPlanner.FollowConfirmedEntry(assignments.ToImmutable(), leadContentId, plot);
        var updated = members.ToBuilder();
        var assignmentIndex = 0;
        for (var index = 0; index < updated.Count; index++)
        {
            if (updated[index].Assignment is null)
            {
                continue;
            }

            if (updated[index].Waiting)
            {
                updated[index] = updated[index] with { Assignment = followed[assignmentIndex] };
            }

            assignmentIndex++;
        }

        return updated.MoveToImmutable();
    }

    private static ImmutableArray<BatchMember> Requeue(ImmutableArray<BatchMember> members, DateTime nowUtc)
    {
        var updated = members.ToBuilder();
        for (var index = 0; index < updated.Count; index++)
        {
            if (updated[index].State == BatchMemberState.Running)
            {
                updated[index] = updated[index] with { State = BatchMemberState.Queued, UpdatedUtc = nowUtc };
            }
        }

        return updated.MoveToImmutable();
    }

    private static BatchMember Member(RunPolicy policy, ReadOnlySpan<CharacterProfile> roster, ulong contentId, DateTime nowUtc)
    {
        var settings = SettingsOf(policy, contentId);
        for (var index = 0; index < roster.Length; index++)
        {
            if (roster[index].ContentId == contentId)
            {
                return new BatchMember { ContentId = contentId, Name = roster[index].Name, HomeWorldId = roster[index].HomeWorldId, Settings = settings, UpdatedUtc = nowUtc };
            }
        }

        return new BatchMember { ContentId = contentId, Name = string.Empty, HomeWorldId = 0, Settings = settings, UpdatedUtc = nowUtc };
    }

    private static CharacterRunSettings SettingsOf(RunPolicy policy, ulong contentId)
    {
        for (var index = 0; index < policy.Characters.Length; index++)
        {
            if (policy.Characters[index].ContentId == contentId)
            {
                return policy.Characters[index];
            }
        }

        throw new ArgumentException("Every member of a batch comes from its policy.", nameof(contentId));
    }
}
