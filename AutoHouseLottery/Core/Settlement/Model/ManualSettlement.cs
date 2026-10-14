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

            // A crash or a logout right after Yes can leave the receipt anywhere on its refund path, short of the
            // reconciliation state the visit would have written.
            var open = SettlementLookups.OpenAttempt(snapshot, receipt.ReceiptId);
            var refundInQuestion = open is { Kind: ActionKind.Refund, State: AttemptState.NeedsReconciliation }
                && (settlement.State == SettlementState.NeedsReconciliation || SettlementStates.OnRefundPath(settlement.State));
            if ((open is null && SettlementStates.RefundOwed(settlement.State)) || refundInQuestion)
            {
                fixes.Add(new SettlementFix(receipt.ReceiptId, SettlementState.Refunded, open?.AttemptId));
            }
        }

        return fixes.ToImmutable();
    }

    // The plugin's own refund or claim was confirmed, but the receipt never reached Refunded or Claimed because the plugin
    // stopped between the two writes (a crash, a logout, an unload). Only the states such a stop leaves behind are
    // repaired: a refund's RefundPending or NeedsReconciliation, a claim's ClaimPending or NeedsReconciliation. A receipt in
    // any other state got there afterwards, such as one a LogMessage 6100 refusal reopened, and is left to be read again.
    public static ImmutableArray<SettlementFix> FromOwnConfirmations(in JournalSnapshot snapshot, ulong contentId)
    {
        var fixes = ImmutableArray.CreateBuilder<SettlementFix>();
        var attempts = snapshot.Attempts;
        for (var index = 0; index < attempts.Length; index++)
        {
            var attempt = attempts[index];
            if (attempt is not { State: AttemptState.Confirmed, Kind: ActionKind.Refund or ActionKind.Claim, ReceiptId: { } receiptId }
                || attempt.CharacterContentId != contentId
                || SettlementLookups.Settlement(snapshot, receiptId) is not { } settlement
                || !LeftBehindByPress(attempt.Kind, settlement.State))
            {
                continue;
            }

            fixes.Add(new SettlementFix(receiptId, attempt.Kind == ActionKind.Refund ? SettlementState.Refunded : SettlementState.Claimed, null));
        }

        return fixes.ToImmutable();
    }

    private static bool LeftBehindByPress(ActionKind kind, SettlementState state) => kind == ActionKind.Refund
        ? state is SettlementState.RefundPending or SettlementState.NeedsReconciliation
        : state is SettlementState.ClaimPending or SettlementState.NeedsReconciliation;

    // The player's word that a refund owed, or a result never read, was settled in game by hand. Only for an entry of
    // this plugin whose results are out and that has no refund or claim of the plugin still in question; never for an
    // earlier attempt still in question, which only its plot's result can settle.
    public static bool CanMarkSettledInGame(SettlementState state, bool resultsArrived, bool attemptOpen)
        => resultsArrived && !attemptOpen && (SettlementStates.OutcomeOpen(state) || SettlementStates.RefundOwed(state));

    // The entries of a character the game says are not settled after all (LogMessage 6100): those recorded as settled
    // without a result being read.
    public static ImmutableArray<Guid> ReopenedBy6100(in JournalSnapshot snapshot, ulong contentId)
    {
        var reopened = ImmutableArray.CreateBuilder<Guid>();
        var receipts = snapshot.Receipts;
        for (var index = 0; index < receipts.Length; index++)
        {
            var receipt = receipts[index];
            if (receipt.CharacterContentId == contentId && SettlementLookups.Settlement(snapshot, receipt.ReceiptId) is { SettledByAbsence: true })
            {
                reopened.Add(receipt.ReceiptId);
            }
        }

        return reopened.ToImmutable();
    }
}

internal static class PendingRules
{
    // An entry nothing here can settle: the visit read no outcome, the placard offered no result, or the plot the
    // Timers text names cannot be told. Such an entry is left to the game and never blocks a new one.
    public static bool LeftToTheGame(SettlementState state, bool noResultSeen, bool plotKnown)
        => state == SettlementState.OutcomeUnknown || noResultSeen || !plotKnown;
}
