using System.Collections.Immutable;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence.Model;

namespace AutoHouseLottery.Core.Integration.Model;

// UnsettledReceipt: an entry from an earlier cycle whose result nobody has read. UncertainAttempt: an attempt from an
// earlier cycle whose press may or may not have reached the game; only the plot's result can tell, since that cycle's
// lottery reading is gone.
internal enum ResultCheckSource : byte { UnsettledReceipt, UncertainAttempt }

internal readonly record struct ResultCheck(Guid Id, ResultCheckSource Source, ulong ContentId, CycleKey Cycle, PlotKey? Plot);

// The settlement items the result inspection works through. An uncertain attempt from an earlier cycle cannot hold a
// later cycle's entry slot, so it lands here instead of blocking new entries; if a refund is really outstanding, the
// game refuses the next entry itself (LogMessage 6100).
internal static class ResultChecks
{
    public static ImmutableArray<ResultCheck> Due(in JournalSnapshot snapshot, CycleKey current)
    {
        var due = ImmutableArray.CreateBuilder<ResultCheck>();
        var receipts = snapshot.Receipts;
        for (var index = 0; index < receipts.Length; index++)
        {
            var receipt = receipts[index];
            if (JournalLookups.TryFindSettlement(snapshot, receipt.ReceiptId, out var settlement) && IsUnsettledFromEarlierCycle(settlement.State, receipt.Cycle, current))
            {
                due.Add(new ResultCheck(receipt.ReceiptId, ResultCheckSource.UnsettledReceipt, receipt.CharacterContentId, receipt.Cycle, receipt.Plot));
            }
        }

        var attempts = snapshot.Attempts;
        for (var index = 0; index < attempts.Length; index++)
        {
            var attempt = attempts[index];
            if (IsUncertainFromEarlierCycle(attempt, current))
            {
                due.Add(new ResultCheck(attempt.AttemptId, ResultCheckSource.UncertainAttempt, attempt.CharacterContentId, attempt.Cycle, attempt.Plot));
            }
        }

        return due.ToImmutable();
    }

    // A receipt whose outcome nobody has read yet, from a cycle whose results are out.
    public static bool IsUnsettledFromEarlierCycle(SettlementState state, CycleKey entered, CycleKey current)
        => state is SettlementState.Entered
                or SettlementState.ResultsPending
                or SettlementState.InspectingResult
                or SettlementState.OutcomeUnknown
                or SettlementState.NeedsReconciliation
            && IsEarlier(entered, current);

    public static bool IsUncertainFromEarlierCycle(ActionAttempt attempt, CycleKey current)
        => attempt.Kind == ActionKind.Entry
            && attempt.State is AttemptState.Prepared or AttemptState.Dispatched or AttemptState.NeedsReconciliation
            && IsEarlier(attempt.Cycle, current);

    private static bool IsEarlier(CycleKey entered, CycleKey current) => entered.Region == current.Region && current.IsLaterThan(entered);
}
