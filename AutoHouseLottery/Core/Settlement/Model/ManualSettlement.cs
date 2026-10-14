using System.Collections.Immutable;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence.Model;
using LotteryPhase = AutoHouseLottery.Core.Housing.Model.LotteryPhase;

namespace AutoHouseLottery.Core.Settlement.Model;

// A settlement the journal can record without a visit: the receipt reaches Target, and the plugin's own refund or claim
// still open on it, if any, is confirmed with it.
internal readonly record struct SettlementFix(Guid ReceiptId, SettlementState Target, Guid? ConfirmAttempt);

// Entries the player settled outside the plugin, or that the plugin's own press settled without a confirmation at the
// time, recognised from what the game shows of the character, so no second action and no missing deposit follows.
internal static class ManualSettlement
{
    // The character's estate list holds the entered plot: the win was claimed. Never applied to a refund on record,
    // which would contradict it.
    public static ImmutableArray<SettlementFix> FromEstates(in JournalSnapshot snapshot, ulong contentId, EstateAddress? personalEstate, EstateAddress? freeCompanyEstate)
    {
        var fixes = ImmutableArray.CreateBuilder<SettlementFix>();
        var receipts = snapshot.Receipts;
        for (var index = 0; index < receipts.Length; index++)
        {
            var receipt = receipts[index];
            if (receipt.CharacterContentId != contentId
                || receipt.Plot is not { } plot
                || SettlementLookups.Settlement(snapshot, receipt.ReceiptId) is not { } settlement
                || settlement.IsSettled
                || SettlementStates.OnRefundPath(settlement.State)
                || !ClaimRules.Owns(receipt.Buyer, personalEstate, freeCompanyEstate, plot))
            {
                continue;
            }

            var open = SettlementLookups.OpenAttempt(snapshot, receipt.ReceiptId);
            if (open is { Kind: ActionKind.Refund })
            {
                continue;
            }

            fixes.Add(new SettlementFix(receipt.ReceiptId, SettlementState.Claimed, open?.AttemptId));
        }

        return fixes.ToImmutable();
    }

    // A fresh reading from the validated lottery record that shows no entry, taken during the results period of a cycle
    // in which the character's entry had already been found to owe a refund: the refund was collected. Only the record
    // can say "no entry"; the Timers text cannot, and an older cycle's entry may simply have left the record. A press a
    // running visit has just made (Prepared or Dispatched) is that visit's own to confirm, with its amount, so only one
    // already set aside as in question is confirmed here.
    public static ImmutableArray<SettlementFix> FromReading(in JournalSnapshot snapshot, in LotterySnapshot reading, in CyclePosition position)
    {
        if (reading is not { State: LotterySnapshotState.LoadedNone, Source: LotterySource.Record } || position.Phase != LotteryPhase.Results)
        {
            return [];
        }

        var fixes = ImmutableArray.CreateBuilder<SettlementFix>();
        var receipts = snapshot.Receipts;
        for (var index = 0; index < receipts.Length; index++)
        {
            var receipt = receipts[index];
            if (receipt.CharacterContentId != reading.ContentId
                || receipt.Cycle != position.Cycle
                || SettlementLookups.Settlement(snapshot, receipt.ReceiptId) is not { } settlement)
            {
                continue;
            }

            var open = SettlementLookups.OpenAttempt(snapshot, receipt.ReceiptId);
            var refundInQuestion = open is { Kind: ActionKind.Refund, State: AttemptState.NeedsReconciliation } && settlement.State == SettlementState.NeedsReconciliation;
            if ((open is null && SettlementStates.RefundOwed(settlement.State)) || refundInQuestion)
            {
                fixes.Add(new SettlementFix(receipt.ReceiptId, SettlementState.Refunded, open?.AttemptId));
            }
        }

        return fixes.ToImmutable();
    }
}
