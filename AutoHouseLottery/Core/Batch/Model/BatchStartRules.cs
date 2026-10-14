using AutoHouseLottery.Core.Selection.Model;
using AutoHouseLottery.Core.Tasks.Model;

namespace AutoHouseLottery.Core.Batch.Model;

// Facts the controller reads before a batch takes the lease. Blockers and PolicyError are the batch preview's.
// LoggedInAssigned: the logged-in character is one of the planned entries. LoggedInPending: that character has an entry
// attempt still in question.
internal readonly record struct BatchStartFacts(
    bool Running,
    bool OtherWorkBusy,
    bool LoggedIn,
    bool AutoAnswerLoaded,
    bool InContent,
    bool ShortlistPolicy,
    RunPolicyError PolicyError,
    RunBlocker Blockers,
    bool JournalWritable,
    int Assignments,
    bool LoggedInAssigned,
    bool LoggedInPending,
    bool SwitchingAllowed);

internal static class BatchStartRules
{
    // The single-run checks, applied to the whole batch. The cycle is confirmed at each placard, so an estimated cycle does
    // not stop a start. Without switching, the batch starts only from one of its own characters, since nothing else could
    // run now.
    public static StartRefusal Check(in BatchStartFacts facts)
    {
        if (facts.Running)
        {
            return StartRefusal.AlreadyRunning;
        }

        if (facts.OtherWorkBusy)
        {
            return StartRefusal.Busy;
        }

        if (!facts.LoggedIn)
        {
            return StartRefusal.NotLoggedIn;
        }

        if (facts.AutoAnswerLoaded)
        {
            return StartRefusal.AutoAnswerPlugin;
        }

        if (facts.InContent)
        {
            return StartRefusal.InContent;
        }

        if (!facts.ShortlistPolicy)
        {
            return StartRefusal.WrongMode;
        }

        if (facts.PolicyError != RunPolicyError.None || (facts.Blockers & (RunBlocker.InvalidPolicy | RunBlocker.DepositTotalOverflow)) != 0)
        {
            return StartRefusal.InvalidPolicy;
        }

        if (!facts.JournalWritable)
        {
            return StartRefusal.JournalUnavailable;
        }

        if ((facts.Blockers & (RunBlocker.EntryPeriodClosed | RunBlocker.CycleExpired)) != 0)
        {
            return StartRefusal.EntryPeriodClosed;
        }

        if ((facts.Blockers & RunBlocker.MultipleWinsNeedConfirmation) != 0)
        {
            return StartRefusal.MultipleWinsNeedConfirmation;
        }

        if (facts.Assignments == 0)
        {
            return StartRefusal.NoAssignment;
        }

        if (!facts.LoggedInAssigned && !facts.SwitchingAllowed)
        {
            return StartRefusal.CharacterNotInPolicy;
        }

        return facts.LoggedInAssigned && facts.LoggedInPending ? StartRefusal.ReconciliationPending : StartRefusal.None;
    }
}
