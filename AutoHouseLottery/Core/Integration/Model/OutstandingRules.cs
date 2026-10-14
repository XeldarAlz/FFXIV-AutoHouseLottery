using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence.Model;

namespace AutoHouseLottery.Core.Integration.Model;

// In rising order of what it asks of the player. ResultUnchecked: an entry from an earlier cycle whose result nobody has
// looked at yet holds a deposit the game wants settled before the next entry, whether it turns out a refund or a win.
internal enum OutstandingKind : byte { None, ResultUnchecked, RefundDue, UnclaimedWin }

internal readonly record struct OutstandingEntry(OutstandingKind Kind, Guid? ReceiptId);

internal static class OutstandingRules
{
    // leftToTheGame names the entries nothing here can settle (no result offered, an outcome the visit could not read, a
    // plot the Timers text does not name for certain): they never block a new entry, and the game itself refuses one
    // while a refund or a plot really waits (LogMessage 6100). A known win always counts.
    public static OutstandingEntry Find(in JournalSnapshot snapshot, ulong contentId, CycleKey currentCycle, Func<EntryReceipt, EntrySettlement, bool> leftToTheGame)
    {
        var found = new OutstandingEntry(OutstandingKind.None, null);
        var receipts = snapshot.Receipts;
        for (var index = 0; index < receipts.Length; index++)
        {
            var receipt = receipts[index];
            if (receipt.CharacterContentId != contentId || !JournalLookups.TryFindSettlement(snapshot, receipt.ReceiptId, out var settlement))
            {
                continue;
            }

            var kind = KindOf(settlement.State, receipt.Cycle, currentCycle);
            if (kind is OutstandingKind.RefundDue or OutstandingKind.ResultUnchecked && leftToTheGame(receipt, settlement))
            {
                continue;
            }

            if (kind > found.Kind)
            {
                found = new OutstandingEntry(kind, receipt.ReceiptId);
            }
        }

        return found;
    }

    // The journal knows only the entries this plugin made or imported, so finding none leaves the question to the game.
    public static OutstandingSettlement ToSettlement(OutstandingKind kind) => kind switch
    {
        OutstandingKind.UnclaimedWin => OutstandingSettlement.UnclaimedWin,
        OutstandingKind.RefundDue or OutstandingKind.ResultUnchecked => OutstandingSettlement.RefundDue,
        _ => OutstandingSettlement.Unknown,
    };

    private static OutstandingKind KindOf(SettlementState state, CycleKey entered, CycleKey current)
    {
        switch (state)
        {
            case SettlementState.WonUnclaimed:
            case SettlementState.ClaimPending:
                return OutstandingKind.UnclaimedWin;
            case SettlementState.LostRefundDue:
            case SettlementState.ForfeitRefundDue:
            case SettlementState.RefundPending:
                return OutstandingKind.RefundDue;
            case SettlementState.Claimed:
            case SettlementState.Refunded:
            case SettlementState.Expired:
                return OutstandingKind.None;
            default:
                return ResultChecks.IsUnsettledFromEarlierCycle(state, entered, current) ? OutstandingKind.ResultUnchecked : OutstandingKind.None;
        }
    }
}
