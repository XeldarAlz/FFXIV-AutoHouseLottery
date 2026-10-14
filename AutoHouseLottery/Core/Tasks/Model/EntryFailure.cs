namespace AutoHouseLottery.Core.Tasks.Model;

// Why a run stopped short of an entry for a character, or why it stopped looking at a plot.
internal enum EntryFailure : byte
{
    None,

    // Settled by the plot's own live state before any payment: the only failures a best match may move on from.
    Sold,
    WrongPhase,
    NotLottery,
    ChangedPrice,
    OverBudget,
    Ineligible,

    // Not conclusive about the plot: the player looks, then retries or resumes.
    TravelFailed,
    TravelUnavailable,
    DependencyBusy,
    WrongDestination,
    PlacardNotFound,
    PlacardNotOpened,
    WrongPlacard,
    IdentityUnverified,
    SaleUnreadable,
    EntryButtonUnavailable,
    ConfirmationNotShown,
    DepositUnreadable,
    GilUnknown,
    CheckboxNotTicked,
    ConfirmRefused,
    RealEntryNotCovered,

    // About the character or the run rather than the plot.
    Rejected,
    AlreadyEntered,
    CollectRefundFirst,
    CheckEarlierResult,
    UnclaimedWin,
    CycleChanged,
    JournalUnavailable,
    CharacterChanged,
    InspectionsExhausted,

    // A press may have reached the game.
    SubmissionUncertain,
}

internal enum FallbackDecision : byte
{
    TryNextPlot,
    FinishCharacter,
    AskPlayer,
}

internal static class FallbackRules
{
    public static bool IsConclusivePreCommit(EntryFailure failure)
        => failure is EntryFailure.Sold
            or EntryFailure.WrongPhase
            or EntryFailure.NotLottery
            or EntryFailure.ChangedPrice
            or EntryFailure.OverBudget
            or EntryFailure.Ineligible;

    // A best match moves to the next ranked plot only after a conclusive failure before payment, within its inspection
    // limit. An exact plot never moves. Once a press may have reached the game nothing moves.
    public static FallbackDecision Decide(RunMode mode, EntryFailure failure, bool submissionStarted, int inspectionsUsed, int maxInspections, bool hasNextTarget)
    {
        if (submissionStarted)
        {
            return FallbackDecision.AskPlayer;
        }

        if (!IsConclusivePreCommit(failure))
        {
            return IsAboutTheCharacter(failure) ? FallbackDecision.FinishCharacter : FallbackDecision.AskPlayer;
        }

        if (mode == RunMode.BestMatch && hasNextTarget && inspectionsUsed < maxInspections)
        {
            return FallbackDecision.TryNextPlot;
        }

        return FallbackDecision.FinishCharacter;
    }

    public static bool IsAboutTheCharacter(EntryFailure failure)
        => failure is EntryFailure.Rejected
            or EntryFailure.AlreadyEntered
            or EntryFailure.CollectRefundFirst
            or EntryFailure.CheckEarlierResult
            or EntryFailure.UnclaimedWin
            or EntryFailure.CycleChanged
            or EntryFailure.CharacterChanged
            or EntryFailure.InspectionsExhausted;
}
