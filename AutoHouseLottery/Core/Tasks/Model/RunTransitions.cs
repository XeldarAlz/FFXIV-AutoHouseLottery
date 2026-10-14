namespace AutoHouseLottery.Core.Tasks.Model;

internal static class RunTransitions
{
    public static bool IsLegal(EntryStep from, EntryStep to)
    {
        if (EntrySteps.MayPauseAt(from) && to is EntryStep.Paused or EntryStep.Stopped or EntryStep.NeedsPlayer)
        {
            return true;
        }

        return from switch
        {
            EntryStep.Ready => to is EntryStep.Preflight or EntryStep.Paused or EntryStep.Stopped,
            EntryStep.Preflight => to is EntryStep.SettleOutstanding or EntryStep.NeedsReconciliation or EntryStep.FinishCharacter,
            EntryStep.SettleOutstanding => to is EntryStep.ReserveFunds or EntryStep.FinishCharacter,
            EntryStep.ReserveFunds => to is EntryStep.Travel or EntryStep.FinishCharacter,
            EntryStep.Travel => to is EntryStep.VerifyArrival or EntryStep.Travel,
            EntryStep.VerifyArrival => to is EntryStep.OpenPlacard or EntryStep.Travel,
            EntryStep.OpenPlacard => to is EntryStep.ReadLivePlot,
            EntryStep.ReadLivePlot => to is EntryStep.VerifyEligibility or EntryStep.Travel or EntryStep.FinishCharacter,
            EntryStep.VerifyEligibility => to is EntryStep.PrepareEntry or EntryStep.Travel or EntryStep.FinishCharacter,
            EntryStep.PrepareEntry => to is EntryStep.PersistIntent or EntryStep.Travel or EntryStep.FinishCharacter,
            EntryStep.PersistIntent => to is EntryStep.SubmitOnce or EntryStep.NeedsPlayer or EntryStep.Stopped,
            EntryStep.SubmitOnce => to is EntryStep.AwaitReceipt or EntryStep.NeedsPlayer or EntryStep.NeedsReconciliation,
            EntryStep.AwaitReceipt => to is EntryStep.PersistReceipt or EntryStep.FinishCharacter or EntryStep.NeedsReconciliation,
            EntryStep.PersistReceipt => to is EntryStep.FinishCharacter or EntryStep.NeedsReconciliation,
            EntryStep.FinishCharacter => to is EntryStep.Completed or EntryStep.CompletedPartial,
            EntryStep.Paused => to is EntryStep.Preflight or EntryStep.Stopped,
            EntryStep.NeedsPlayer => to is EntryStep.Preflight or EntryStep.Stopped,
            _ => false,
        };
    }

    // What an interruption the run cannot wait out (logout, unload) leaves behind: a press that may have reached the
    // game becomes a question for reconciliation, never a stop that looks settled.
    public static EntryStep OnInterrupt(EntryStep step)
    {
        if (EntrySteps.IsTerminal(step))
        {
            return step;
        }

        return EntrySteps.InSubmissionWindow(step) ? EntryStep.NeedsReconciliation : EntryStep.Stopped;
    }
}
