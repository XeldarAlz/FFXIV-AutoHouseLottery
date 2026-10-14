using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence.Model;

namespace AutoHouseLottery.Core.Integration.Model;

internal static class ImportedEntries
{
    // A receipt for an entry the reading shows in the cycle's own phase. The deposit and the time of entry are never
    // in the reading, so they stay unknown, and so do the plot and the buyer when the Timers text was all there was.
    public static EntryReceipt? From(Guid receiptId, ulong contentId, uint homeWorldId, in LotterySnapshot reading, in CyclePosition position)
    {
        if (reading.State != LotterySnapshotState.Active
            || reading.ContentId != contentId
            || reading.Entry is not { LotteryNumber: { } number } entry
            || !ParticipationRules.SamePhase(entry.Phase, position.Phase))
        {
            return null;
        }

        return new EntryReceipt
        {
            ReceiptId = receiptId,
            CharacterContentId = contentId,
            HomeWorldId = homeWorldId,
            Cycle = position.Cycle,
            Plot = ParticipationRules.PlotOf(entry, position.Cycle.Region, homeWorldId),
            Buyer = entry.BuyerType switch
            {
                LotteryBuyerType.Private => BuyerType.Private,
                LotteryBuyerType.FreeCompany => BuyerType.FreeCompany,
                _ => BuyerType.Unknown,
            },
            Evidence = ReceiptEvidence.ImportedFromGame,
            EntryNumber = number <= int.MaxValue ? (int)number : null,
            AddressText = entry.AddressText,
        };
    }
}
