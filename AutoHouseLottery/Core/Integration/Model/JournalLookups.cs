using AutoHouseLottery.Core.Persistence.Model;

namespace AutoHouseLottery.Core.Integration.Model;

internal static class JournalLookups
{
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
