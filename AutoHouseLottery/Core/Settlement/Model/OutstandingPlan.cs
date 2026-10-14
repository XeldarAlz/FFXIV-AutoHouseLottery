using AutoHouseLottery.Core.Integration.Model;

namespace AutoHouseLottery.Core.Settlement.Model;

// What the entry run's SettleOutstanding step does about an earlier entry the game may still hold a deposit or a plot
// for (LogMessage 6100): go on, collect it first through the refund driver, or stop and say which.
internal enum OutstandingStep : byte
{
    Proceed,
    CollectFirst,
    StopCollectRefundFirst,
    StopUnclaimedWin,
    StopDryRunCollection,
}

internal static class OutstandingPlan
{
    // A known win stops for the player; a refund known to be owed is collected first when automatic refunds are on and
    // stops the run otherwise. A result nobody has read is visited first when automatic refunds are on; otherwise it
    // does not hold the run up, since the game refuses the entry itself if something really waits.
    public static OutstandingStep Decide(OutstandingKind kind, bool automaticRefunds) => kind switch
    {
        OutstandingKind.UnclaimedWin => OutstandingStep.StopUnclaimedWin,
        OutstandingKind.RefundDue => automaticRefunds ? OutstandingStep.CollectFirst : OutstandingStep.StopCollectRefundFirst,
        OutstandingKind.ResultUnchecked when automaticRefunds => OutstandingStep.CollectFirst,
        _ => OutstandingStep.Proceed,
    };

    // After the refund driver's visit, from what is still outstanding then. A dry run cannot free the slot, so the run
    // stops and says so; anything the visit could not settle is decided as if automatic refunds were off, so the same
    // entry is never visited twice in one run.
    public static OutstandingStep AfterCollection(SettlementResult result, OutstandingKind remaining)
        => result == SettlementResult.DryRun ? OutstandingStep.StopDryRunCollection : Decide(remaining, automaticRefunds: false);
}
