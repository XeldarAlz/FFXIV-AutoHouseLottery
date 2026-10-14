using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence.Model;

namespace AutoHouseLottery.Core.Integration.Model;

internal static class JournalLookups
{
    // An entry attempt of this cycle that may have reached the game and is not settled yet.
    public static bool HasPendingEntry(in JournalSnapshot snapshot, ulong contentId, CycleKey cycle)
    {
        var attempts = snapshot.Attempts;
        for (var index = 0; index < attempts.Length; index++)
        {
            var attempt = attempts[index];
            if (attempt.Kind == ActionKind.Entry
                && attempt.CharacterContentId == contentId
                && attempt.Cycle == cycle
                && attempt.State is AttemptState.Prepared or AttemptState.Dispatched or AttemptState.NeedsReconciliation)
            {
                return true;
            }
        }

        return false;
    }

    // The deposit of the entry holding the character's slot for the cycle, null when the record does not know it.
    public static long? EntryDeposit(in JournalSnapshot snapshot, ulong contentId, uint homeWorldId, CycleKey cycle)
    {
        var receipts = snapshot.Receipts;
        for (var index = 0; index < receipts.Length; index++)
        {
            if (receipts[index].HoldsSlotOf(contentId, homeWorldId, cycle))
            {
                return receipts[index].Deposit;
            }
        }

        return null;
    }

    public static bool TryFindSettlement(in JournalSnapshot snapshot, Guid receiptId, out EntrySettlement settlement)
    {
        var settlements = snapshot.Settlements;
        for (var index = 0; index < settlements.Length; index++)
        {
            if (settlements[index].ReceiptId == receiptId)
            {
                settlement = settlements[index];
                return true;
            }
        }

        settlement = default;
        return false;
    }
}
