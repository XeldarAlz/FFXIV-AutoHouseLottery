using AutoHouseLottery.Core.Integration.Model;
using AutoHouseLottery.Core.Persistence.Model;

namespace AutoHouseLottery.Core.Settlement.Model;

internal static class SettlementLookups
{
    public static EntryReceipt? Receipt(in JournalSnapshot snapshot, Guid receiptId)
    {
        var receipts = snapshot.Receipts;
        for (var index = 0; index < receipts.Length; index++)
        {
            if (receipts[index].ReceiptId == receiptId)
            {
                return receipts[index];
            }
        }

        return null;
    }

    public static ActionAttempt? Attempt(in JournalSnapshot snapshot, Guid attemptId)
    {
        var attempts = snapshot.Attempts;
        for (var index = 0; index < attempts.Length; index++)
        {
            if (attempts[index].AttemptId == attemptId)
            {
                return attempts[index];
            }
        }

        return null;
    }

    public static EntrySettlement? Settlement(in JournalSnapshot snapshot, Guid receiptId)
        => JournalLookups.TryFindSettlement(snapshot, receiptId, out var settlement) ? settlement : null;

    // A refund or claim of this plugin on the receipt that was written and not yet settled either way: it may have
    // reached the game, so nothing may press again until it is resolved.
    public static ActionAttempt? OpenAttempt(in JournalSnapshot snapshot, Guid receiptId)
    {
        var attempts = snapshot.Attempts;
        for (var index = 0; index < attempts.Length; index++)
        {
            var attempt = attempts[index];
            if (attempt.Kind != ActionKind.Entry && attempt.ReceiptId == receiptId && IsOpen(attempt.State))
            {
                return attempt;
            }
        }

        return null;
    }

    public static bool IsOpen(AttemptState state) => state is AttemptState.Prepared or AttemptState.Dispatched or AttemptState.NeedsReconciliation;
}

internal static class SettlementStates
{
    // Nothing about the result is known yet.
    public static bool OutcomeOpen(SettlementState state)
        => state is SettlementState.Entered or SettlementState.ResultsPending or SettlementState.InspectingResult or SettlementState.OutcomeUnknown;

    public static bool RefundOwed(SettlementState state) => state is SettlementState.LostRefundDue or SettlementState.ForfeitRefundDue;

    public static bool OnRefundPath(SettlementState state) => RefundOwed(state) || state == SettlementState.RefundPending;

    public static bool OnWinPath(SettlementState state) => state is SettlementState.WonUnclaimed or SettlementState.ClaimPending;
}
