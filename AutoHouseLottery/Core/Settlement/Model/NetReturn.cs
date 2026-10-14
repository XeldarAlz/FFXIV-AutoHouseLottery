using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Shared;

namespace AutoHouseLottery.Core.Settlement.Model;

// RefundReceived: gil the game returned, as measured. TravelSpent: gil spent travelling for settlements. NetReturned:
// the refunds less the travel, which can be negative. StillPending: deposits not yet returned on entries that did not
// win. AmountsUnknown: settled refunds and pending deposits whose amount was never seen, left out of the sums rather
// than guessed.
internal readonly record struct NetReturnSummary(long RefundReceived, long TravelSpent, long NetReturned, long StillPending, int AmountsUnknown);

internal static class NetReturn
{
    // A won deposit pays for the plot, so wins and claims count in none of the sums; their travel still does.
    public static NetReturnSummary Of(in JournalSnapshot snapshot)
    {
        long received = 0;
        long travel = 0;
        long pending = 0;
        var unknown = 0;
        var receipts = snapshot.Receipts;
        for (var index = 0; index < receipts.Length; index++)
        {
            var receipt = receipts[index];
            if (SettlementLookups.Settlement(snapshot, receipt.ReceiptId) is not { } settlement)
            {
                continue;
            }

            travel = Gil.SaturatingAdd(travel, settlement.TravelSpent);
            switch (settlement.State)
            {
                case SettlementState.Refunded when settlement.ReceivedRefund is { } amount:
                    received = Gil.SaturatingAdd(received, amount);
                    break;
                case SettlementState.Refunded:
                    unknown++;
                    break;
                case var _ when Unreturned(settlement) && (settlement.ExpectedRefund ?? receipt.Deposit) is { } held:
                    pending = Gil.SaturatingAdd(pending, held);
                    break;
                case var _ when Unreturned(settlement):
                    unknown++;
                    break;
            }
        }

        var net = Gil.TrySubtract(received, travel, out var difference) ? difference : long.MinValue;
        return new NetReturnSummary(received, travel, net, pending, unknown);
    }

    // A refund in question still counts as pending; a claim in question does not, since its deposit paid for the plot.
    private static bool Unreturned(in EntrySettlement settlement)
        => SettlementStates.OutcomeOpen(settlement.State)
        || SettlementStates.OnRefundPath(settlement.State)
        || (settlement.State == SettlementState.NeedsReconciliation && settlement.Refund != RefundKind.None);
}
