using System.Collections.Immutable;
using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Integration.Model;
using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Selection.Model;
using AutoHouseLottery.Core.Shared;
using AutoHouseLottery.Core.Tasks.Model;
using AutoHouseLottery.Core.Travel.Model;

namespace AutoHouseLottery.Core.Batch.Model;

// Held: every member still waiting belongs to an ownership group with an entry in question, so none may go yet.
internal enum BatchStepKind : byte { Process, Switch, WaitForLogin, Held, Finished }

internal readonly record struct BatchStep(BatchStepKind Kind, int MemberIndex);

// RequestedCharacter: the character the batch switched to. QueuedCharacter: a member still waiting logged in on its own.
// WrongCharacter: somebody else logged in while the batch waited for a switch. WrongHomeWorld: the right content ID with
// another home world than the batch recorded.
internal enum LoginVerdict : byte { NotInBatch, RequestedCharacter, QueuedCharacter, WrongCharacter, WrongHomeWorld }

internal readonly record struct BatchReport(int Entered, int NotEntered, int Excluded, int Uncertain, int Queued, int Running, long Deposited);

// The batch of TECHNICAL_DESIGN sections 10 and 14 as plain data: each selected character makes at most one entry, one
// after another, through the same entry run as a single character; nothing is pooled between them, and every step that
// changes the batch returns a new checkpoint.
internal static class BatchRules
{
    // Members freed by the journal go back in the queue before the entries it confirms are recorded, so they follow those.
    private const int RequeuePass = 0;
    private const int RecordPass = 1;
    private const int ReevaluationPasses = 2;

    public static RunMode ModeOf(in RunAssignment assignment) => assignment.Exact ? RunMode.Exact : RunMode.BestMatch;

    // The preview's assignments in their planned order, exact targets first, then the characters the plan left out.
    public static BatchCheckpoint Create(Guid batchId, RunPolicy policy, RunPreview preview, ReadOnlySpan<CharacterProfile> roster, DateTime nowUtc)
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
            Members = members.MoveToImmutable(),
            Phase = BatchPhase.Running,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
        };
    }

    // The logged-in character goes first when it is still waiting, so a switch is never spent on it. Otherwise the next
    // waiting member is switched to when switching is allowed, or waits for its own login when it is not. A member whose
    // ownership group has an entry in question is held until that entry is read back, since the group's plot is not known.
    public static BatchStep Next(BatchCheckpoint checkpoint, ulong? loggedIn, bool switchingAllowed)
    {
        var firstWaiting = -1;
        var firstHeld = -1;
        for (var index = 0; index < checkpoint.Members.Length; index++)
        {
            var member = checkpoint.Members[index];
            if (!member.Waiting)
            {
                continue;
            }

            if (Held(checkpoint, index))
            {
                firstHeld = firstHeld < 0 ? index : firstHeld;
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
            return firstHeld < 0 ? new BatchStep(BatchStepKind.Finished, -1) : new BatchStep(BatchStepKind.Held, firstHeld);
        }

        return switchingAllowed ? new BatchStep(BatchStepKind.Switch, firstWaiting) : new BatchStep(BatchStepKind.WaitForLogin, firstWaiting);
    }

    // A waiting member another member of whose ownership group has an entry in question.
    public static bool Held(BatchCheckpoint checkpoint, int index)
    {
        if (checkpoint.Members[index] is not { Waiting: true, Assignment: { } assignment })
        {
            return false;
        }

        for (var otherIndex = 0; otherIndex < checkpoint.Members.Length; otherIndex++)
        {
            var other = checkpoint.Members[otherIndex];
            if (otherIndex != index && other.State == BatchMemberState.Uncertain && other.Assignment is { } otherAssignment && otherAssignment.Group == assignment.Group)
            {
                return true;
            }
        }

        return false;
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

    // A member a fresh check rules out before its run starts: its slot, an earlier entry still to settle, its gil. One
    // found already entered holds its ownership group on that plot, as an entry the batch made would.
    public static BatchCheckpoint Skip(BatchCheckpoint checkpoint, int index, EligibilityReason reasons, EntryFailure failure, PlotKey? enteredPlot, DateTime nowUtc)
        => Settle(checkpoint, index, checkpoint.Members[index] with
        {
            State = BatchMemberState.NotEntered,
            Reasons = reasons,
            Failure = failure,
            Plot = failure == EntryFailure.AlreadyEntered ? enteredPlot : null,
            UpdatedUtc = nowUtc,
        }, nowUtc);

    // How a member's run ended, for the batch. A run that ended before anything was entered leaves the member waiting
    // when the player stopped it or something cut it short, so a resume starts it again from a fresh check. A press that
    // may have reached the game halts the batch: no switch happens while an entry is in question.
    public static (BatchMemberState State, BatchHalt Halt) Classify(EntryStep step, EntryFailure failure, BatchEnd end)
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
                return (BatchMemberState.Entered, halt);
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
            EntryFailure.AutoAnswerPlugin => (BatchMemberState.Queued, BatchHalt.AutoAnswerPlugin),
            _ => (BatchMemberState.NotEntered, BatchHalt.None),
        };
    }

    // Records a member's outcome. An entry, or a member its own run found already entered, keeps its ownership group on that
    // plot (Same plot and By group placement): members still waiting whose own
    // targets hold it are pinned to it, and every other one, an exact member included, is left out rather than entering a
    // second plot the group could not claim with the first.
    public static BatchCheckpoint Record(BatchCheckpoint checkpoint, int index, BatchMemberState state, EntryFailure failure, PlotKey? plot, long? deposit, DateTime nowUtc)
        => Settle(checkpoint, index, checkpoint.Members[index] with
        {
            State = state,
            Failure = state == BatchMemberState.Entered ? EntryFailure.None : failure,
            Plot = plot,
            Deposit = deposit,
            UpdatedUtc = nowUtc,
        }, nowUtc);

    // A continued batch may only process the members its next step names; a step that holds every waiting member behind
    // an entry in question is the reconciliation the player still owes, and anything else is a character to log in.
    public static StartRefusal ContinueRefusal(BatchStepKind kind) => kind switch
    {
        BatchStepKind.Process or BatchStepKind.Switch => StartRefusal.None,
        BatchStepKind.Held => StartRefusal.ReconciliationPending,
        _ => StartRefusal.CharacterNotInPolicy,
    };

    // A member a fresh check before its run rules out: its character blockers, then its funds against its targets.
    public static (EligibilityReason Reasons, EntryFailure Failure) Recheck(EligibilityReason characterBlockers, BudgetCheck funds)
    {
        var reasons = characterBlockers
            | (funds == BudgetCheck.BelowReserve ? EligibilityReason.InsufficientGil : EligibilityReason.None)
            | (funds is BudgetCheck.OverDepositCap or BudgetCheck.Overflow ? EligibilityReason.DepositOverCap : EligibilityReason.None);
        if (reasons == EligibilityReason.None)
        {
            return (EligibilityReason.None, EntryFailure.None);
        }

        var failure = (reasons & EligibilityReason.AlreadyEnteredThisCycle) != 0 ? EntryFailure.AlreadyEntered
            : (reasons & EligibilityReason.OutstandingRefundOrProperty) != 0 ? EntryFailure.CollectRefundFirst
            : (reasons & (EligibilityReason.InsufficientGil | EligibilityReason.DepositOverCap)) != 0 ? EntryFailure.OverBudget
            : EntryFailure.CharacterIneligible;
        return (reasons, failure);
    }

    // The member's own balance against its whole target order, before its run travels anywhere: it fits when any target it
    // may still enter fits its own cap, reserve and gil. A target with no known deposit, or a balance that cannot be read,
    // leaves the answer to the run's checks at the confirmation; nothing is pooled with another member.
    public static BudgetCheck Affordable(in CharacterBudget budget, ImmutableArray<PlotKey> targets, ImmutableArray<RankedPlot> ranking, long? gil)
    {
        var depositUnknown = false;
        var worst = BudgetCheck.Fits;
        for (var targetIndex = 0; targetIndex < targets.Length; targetIndex++)
        {
            if (DepositOf(ranking, targets[targetIndex]) is not { } deposit)
            {
                depositUnknown = true;
                continue;
            }

            var check = BudgetRules.Check(budget, default, deposit, gil);
            if (check is BudgetCheck.Fits or BudgetCheck.BalanceUnknown)
            {
                return check;
            }

            worst = worst == BudgetCheck.BelowReserve ? worst : check;
        }

        return depositUnknown ? BudgetCheck.DepositUnknown : worst;
    }

    // Members whose entry was in question are looked at again on the player's Continue, from the journal: an entry it now
    // holds is recorded as entered, a slot it shows free again puts the member back in the queue for a fresh check, and
    // an attempt still in question leaves the member as it is.
    public static BatchCheckpoint Reevaluate(BatchCheckpoint checkpoint, in JournalSnapshot journal, DateTime nowUtc)
    {
        var cycle = checkpoint.Policy.Cycle.Cycle;
        var updated = checkpoint;
        for (var pass = 0; pass < ReevaluationPasses; pass++)
        {
            for (var index = 0; index < updated.Members.Length; index++)
            {
                var member = updated.Members[index];
                if (member.State != BatchMemberState.Uncertain || JournalLookups.HasPendingEntry(journal, member.ContentId, cycle))
                {
                    continue;
                }

                var slot = ParticipationRules.FromJournal(journal, member.ContentId, member.HomeWorldId, cycle);
                var entered = slot.State == EntrySlotState.Entered;
                if (pass == RequeuePass && !entered)
                {
                    updated = updated with { Members = updated.Members.SetItem(index, member with { State = BatchMemberState.Queued, Failure = EntryFailure.None, UpdatedUtc = nowUtc }) };
                }
                else if (pass == RecordPass && entered)
                {
                    updated = Record(updated, index, BatchMemberState.Entered, EntryFailure.None, slot.Plot, JournalLookups.EntryDeposit(journal, member.ContentId, member.HomeWorldId, cycle), nowUtc);
                }
            }
        }

        return ReapplyGroupPlots(updated, nowUtc);
    }

    // A member put back in the queue keeps to the plot its group entered while it was in question.
    private static BatchCheckpoint ReapplyGroupPlots(BatchCheckpoint checkpoint, DateTime nowUtc)
    {
        var members = checkpoint.Members;
        for (var index = 0; index < members.Length; index++)
        {
            if (HoldsPlot(members[index]) && members[index].Assignment is { Placement: PlacementMode.SamePlot } lead)
            {
                members = KeepGroupOnPlot(members, lead, members[index].Plot, nowUtc);
            }
        }

        return members == checkpoint.Members ? checkpoint : checkpoint with { Members = members, UpdatedUtc = nowUtc };
    }

    private static BatchCheckpoint Settle(BatchCheckpoint checkpoint, int index, BatchMember member, DateTime nowUtc)
    {
        var members = checkpoint.Members.SetItem(index, member);
        if (HoldsPlot(member) && member.Assignment is { Placement: PlacementMode.SamePlot } lead)
        {
            members = KeepGroupOnPlot(members, lead, member.Plot, nowUtc);
        }

        return checkpoint with { Members = members, UpdatedUtc = nowUtc };
    }

    private static bool HoldsPlot(BatchMember member)
        => member.State == BatchMemberState.Entered
        || (member.State == BatchMemberState.NotEntered && member.Failure == EntryFailure.AlreadyEntered);

    // A switch that did not start because something else is using the game, or Lifestream is busy, is only blocked and
    // waits for a Continue. Anything else that kept it from starting is a failed switch.
    public static BatchHalt SwitchNotStarted(SwitchStartResult result)
        => result is SwitchStartResult.OtherWork or SwitchStartResult.DependencyBusy or SwitchStartResult.AlreadySwitching ? BatchHalt.SwitchBlocked : BatchHalt.SwitchFailed;

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
        var entered = 0;
        var notEntered = 0;
        var excluded = 0;
        var uncertain = 0;
        var queued = 0;
        var running = 0;
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

        return new BatchReport(entered, notEntered, excluded, uncertain, queued, running, deposited);
    }

    // The order the summary lists members in: what was entered first, then what is in question, then what still waits,
    // then what did not enter.
    public static int DisplayRank(BatchMemberState state) => state switch
    {
        BatchMemberState.Entered => 0,
        BatchMemberState.Running => 1,
        BatchMemberState.Uncertain => 2,
        BatchMemberState.Queued => 3,
        BatchMemberState.NotEntered => 4,
        _ => 5,
    };

    // A plot that could not be read leaves every waiting member of the group out: any plot it entered might be a second.
    private static ImmutableArray<BatchMember> KeepGroupOnPlot(ImmutableArray<BatchMember> members, in RunAssignment lead, PlotKey? held, DateTime nowUtc)
    {
        var updated = members;
        for (var index = 0; index < members.Length; index++)
        {
            var member = members[index];
            if (!member.Waiting
                || member.ContentId == lead.ContentId
                || member.Assignment is not { Placement: PlacementMode.SamePlot } assignment
                || assignment.Group != lead.Group)
            {
                continue;
            }

            if (held is not { } plot || assignment.TargetOrder.IndexOf(plot) < 0)
            {
                updated = updated.SetItem(index, member with { State = BatchMemberState.NotEntered, Failure = EntryFailure.GroupEnteredElsewhere, UpdatedUtc = nowUtc });
            }
            else if (!assignment.Exact && assignment.TargetOrder.Length > 1)
            {
                updated = updated.SetItem(index, member with { Assignment = assignment with { TargetOrder = [plot] } });
            }
        }

        return updated;
    }

    private static long? DepositOf(ImmutableArray<RankedPlot> ranking, PlotKey plot)
    {
        for (var index = 0; index < ranking.Length; index++)
        {
            if (ranking[index].Key == plot)
            {
                return ranking[index].Deposit;
            }
        }

        return null;
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
