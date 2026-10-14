namespace AutoHouseLottery.Core.Tasks.Model;

// The entry run of TECHNICAL_DESIGN section 11, one character at a time.
internal enum EntryStep : byte
{
    Ready,
    Preflight,
    SettleOutstanding,
    ReserveFunds,
    Travel,
    VerifyArrival,
    OpenPlacard,
    ReadLivePlot,
    VerifyEligibility,
    PrepareEntry,
    PersistIntent,
    SubmitOnce,
    AwaitReceipt,
    PersistReceipt,
    FinishCharacter,
    Completed,
    CompletedPartial,
    Paused,
    Stopped,
    NeedsPlayer,
    NeedsReconciliation,
}

// The plain steps the run panel names, in order. GettingReady comes before the numbered ones.
internal enum RunStage : byte
{
    GettingReady,
    Travelling,
    Approaching,
    Checking,
    Entering,
    Verifying,
}

internal static class EntrySteps
{
    public const int StageCount = (int)RunStage.Verifying;

    public static RunStage StageOf(EntryStep step) => step switch
    {
        EntryStep.Travel or EntryStep.VerifyArrival => RunStage.Travelling,
        EntryStep.OpenPlacard => RunStage.Approaching,
        EntryStep.ReadLivePlot or EntryStep.VerifyEligibility => RunStage.Checking,
        EntryStep.PrepareEntry or EntryStep.PersistIntent or EntryStep.SubmitOnce => RunStage.Entering,
        EntryStep.AwaitReceipt or EntryStep.PersistReceipt or EntryStep.FinishCharacter => RunStage.Verifying,
        _ => RunStage.GettingReady,
    };

    public static bool IsTerminal(EntryStep step)
        => step is EntryStep.Completed or EntryStep.CompletedPartial or EntryStep.Stopped or EntryStep.NeedsReconciliation;

    // From Confirm to the persisted receipt: a press may have reached the game, so nothing may retry it, and a
    // pause or a stop waits for the verification instead of cutting it short.
    public static bool InSubmissionWindow(EntryStep step) => step is EntryStep.SubmitOnce or EntryStep.AwaitReceipt or EntryStep.PersistReceipt;

    // The intent is written and Confirm follows in the same breath, so nothing may stop the run between them.
    public static bool MayPauseAt(EntryStep step) => step is >= EntryStep.Preflight and <= EntryStep.PrepareEntry;
}
