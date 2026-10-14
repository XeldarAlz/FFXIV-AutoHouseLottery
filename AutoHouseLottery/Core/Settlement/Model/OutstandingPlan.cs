using AutoHouseLottery.Core.Integration.Model;

namespace AutoHouseLottery.Core.Settlement.Model;

// What the entry run's SettleOutstanding step does about an earlier entry the game still holds a deposit or a plot for
// (LogMessage 6100): go on, collect it first through the refund driver, or stop and say which.
internal enum OutstandingStep : byte
{
    Proceed,
    CollectFirst,
    StopCollectRefundFirst,
    StopCheckEarlierResult,
    StopUnclaimedWin,
    StopDryRunCollection,
}

internal static class OutstandingPlan
{
    // A refund owed, or a result nobody has read, is collected first when automatic refunds are on. A win is never
    // collected: it stops the run for the player's decision.
    public static OutstandingStep Decide(OutstandingKind kind, bool automaticRefunds) => kind switch
    {
        OutstandingKind.None => OutstandingStep.Proceed,
        OutstandingKind.UnclaimedWin => OutstandingStep.StopUnclaimedWin,
        OutstandingKind.RefundDue => automaticRefunds ? OutstandingStep.CollectFirst : OutstandingStep.StopCollectRefundFirst,
        _ => automaticRefunds ? OutstandingStep.CollectFirst : OutstandingStep.StopCheckEarlierResult,
    };

    // After the refund driver's visit. A collected or otherwise settled entry lets the run go on, once it has checked
    // again for anything else outstanding. A dry run cannot free the slot, so the run stops and says so.
    public static OutstandingStep AfterCollection(SettlementResult result, InspectionOutcome? outcome)
    {
        switch (result)
        {
            case SettlementResult.Refunded or SettlementResult.RefundedAmountDiffers:
                return OutstandingStep.Proceed;
            case SettlementResult.DryRun:
                return OutstandingStep.StopDryRunCollection;
        }

        return outcome switch
        {
            InspectionOutcome.Refunded or InspectionOutcome.Claimed or InspectionOutcome.Expired when result == SettlementResult.Classified => OutstandingStep.Proceed,
            InspectionOutcome.WonUnclaimed => OutstandingStep.StopUnclaimedWin,
            InspectionOutcome.LostRefundDue or InspectionOutcome.ForfeitRefundDue => OutstandingStep.StopCollectRefundFirst,
            _ => OutstandingStep.StopCheckEarlierResult,
        };
    }
}
