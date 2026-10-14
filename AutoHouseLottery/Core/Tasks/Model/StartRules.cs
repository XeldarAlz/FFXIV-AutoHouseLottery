using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Selection.Model;

namespace AutoHouseLottery.Core.Tasks.Model;

internal enum StartRefusal : byte
{
    None,
    AlreadyRunning,
    Busy,
    NotLoggedIn,
    UnsupportedRegion,
    UnknownRegion,
    InContent,
    WrongMode,
    InvalidPolicy,
    CharacterNotInPolicy,
    JournalUnavailable,
    ReconciliationPending,
    AlreadyEntered,
    EntryPeriodClosed,
    NoAssignment,
    MultipleWinsNeedConfirmation,
    AutoAnswerPlugin,
}

// Facts the controller reads before it takes the lease. Blockers are the preview's, for the logged-in character.
internal readonly record struct StartFacts(
    bool Running,
    bool OtherWorkBusy,
    bool LoggedIn,
    bool InContent,
    RunMode Mode,
    RunPolicy Policy,
    bool CharacterInPolicy,
    bool JournalWritable,
    bool PendingAttempt,
    EntrySlotState Slot,
    RunBlocker Blockers,
    bool CharacterAssigned)
{
    // A plugin that answers dialogs on its own is loaded (AutoAnswerPlugins).
    public bool AutoAnswerLoaded { get; init; }

    // The region of the character's home world (WorldRegions). Left unset it reads as Unknown, which refuses the start.
    public WorldRegion HomeRegion { get; init; }
}

internal static class StartRules
{
    // The cycle is confirmed at the placard itself, so an estimated cycle does not stop a start; every other blocker of
    // the preview does. A character with an attempt still in question cannot start another until it is read back.
    public static StartRefusal Check(in StartFacts facts)
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

        if (!WorldRegions.Supported(facts.HomeRegion))
        {
            return facts.HomeRegion == WorldRegion.Unknown ? StartRefusal.UnknownRegion : StartRefusal.UnsupportedRegion;
        }

        if (facts.AutoAnswerLoaded)
        {
            return StartRefusal.AutoAnswerPlugin;
        }

        if (facts.InContent)
        {
            return StartRefusal.InContent;
        }

        if (facts.Mode == RunMode.Exact ? facts.Policy.Exact is null : facts.Policy.Shortlist is null)
        {
            return StartRefusal.WrongMode;
        }

        if (RunPolicyRules.Validate(facts.Policy) != RunPolicyError.None || (facts.Blockers & RunBlocker.InvalidPolicy) != 0)
        {
            return StartRefusal.InvalidPolicy;
        }

        if (!facts.CharacterInPolicy)
        {
            return StartRefusal.CharacterNotInPolicy;
        }

        if (!facts.JournalWritable)
        {
            return StartRefusal.JournalUnavailable;
        }

        if (facts.PendingAttempt || facts.Slot == EntrySlotState.Unresolved)
        {
            return StartRefusal.ReconciliationPending;
        }

        if (facts.Slot == EntrySlotState.Entered)
        {
            return StartRefusal.AlreadyEntered;
        }

        if ((facts.Blockers & (RunBlocker.EntryPeriodClosed | RunBlocker.CycleExpired)) != 0)
        {
            return StartRefusal.EntryPeriodClosed;
        }

        if ((facts.Blockers & RunBlocker.MultipleWinsNeedConfirmation) != 0)
        {
            return StartRefusal.MultipleWinsNeedConfirmation;
        }

        return facts.CharacterAssigned && (facts.Blockers & ~RunBlocker.CycleNotVerified) == RunBlocker.None ? StartRefusal.None : StartRefusal.NoAssignment;
    }
}
