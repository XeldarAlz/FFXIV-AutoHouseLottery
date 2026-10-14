using System.Threading.Tasks;
using AutoHouseLottery.Core.Game;
using AutoHouseLottery.Core.Game.Character;
using AutoHouseLottery.Core.Game.Dialogs;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Persistence;
using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Settlement.Model;
using AutoHouseLottery.Core.Tasks.Model;
using AutoHouseLottery.Core.Travel.Model;
using ECommons.DalamudServices;

namespace AutoHouseLottery.Core.Tasks;

internal sealed partial class SettlementTask
{
    private const int SettlementGilWaitMs = 30_000;
    private const int OwnershipWaitMs = 30_000;
    private const int SecondLookDelayMs = 60_000;
    private const int PlacardCloseWaitMs = 5_000;

    // A refund the dialog offers on row 7124 or 7123: the gate, the durable intent, one Yes, then the gil and the same
    // placard read again. An offer the recorded deposit does not explain is left to the player.
    private async Task CollectRefund(ActionJournal journal, ResultKind kind, HousingText row, long offered)
    {
        var entry = receipt!;
        var check = RefundRules.CheckOffer(ResultKinds.RefundOf(kind), offered, entry.Deposit);
        Diag($"{Label}: the game offers a {kind} refund of {offered} gil for a deposit of {entry.Deposit?.ToString() ?? "unknown"} gil; {check}.");
        if (check == RefundOffer.DiffersFromDeposit)
        {
            await CloseResult(HousingDialogs.ReadResultDialog());
            End(SettlementResult.NeedsPlayer, SettlementFailure.RefundDiffersFromDeposit);
            return;
        }

        if (!MayPress(ActionKind.Refund))
        {
            await CloseResult(HousingDialogs.ReadResultDialog());
            RunLog.Info($"{Label}: DRY RUN. The refund of {offered} gil for {plot} would have been collected with Yes on Addon row {HousingTextRows.Of(row).RowId}; "
                      + "the dialog was closed instead and nothing was pressed.");
            End(SettlementResult.DryRun);
            return;
        }

        var outcome = await Submit(journal, ActionKind.Refund, row, offered);
        if (outcome is not (SubmissionOutcome.Dispatched or SubmissionOutcome.DispatchUnrecorded))
        {
            await EndUnpressed(outcome);
            return;
        }

        await Advance(journal, entry.ReceiptId, SettlementState.RefundPending, null);
        await AwaitRefund(journal, offered);
    }

    // A win on row 7122 or 7128, only for the player's own click or the dev command: the claim rules, the gate, the
    // durable intent, one Yes, then the character's estate list. Nothing that follows Yes is pressed.
    private async Task Claim(ActionJournal journal, HousingText row)
    {
        var entry = receipt!;
        var snapshot = journal.Snapshot();
        var context = CharacterContextReader.Read();
        var effect = context is { } shown ? ClaimRules.EffectOf(entry.Buyer, shown.FreeCompanyId is not null, shown.PersonalEstate, shown.FreeCompanyEstate) : ClaimEffect.Unknown;
        var refusal = context is null
            ? ClaimRefusal.NotTheEntrant
            : ClaimRules.Check(new ClaimFacts(context.ContentId, entry, SettlementLookups.Settlement(snapshot, entry.ReceiptId)!.Value.State, context.FreeCompanyId is not null,
                context.PersonalEstate, context.FreeCompanyEstate, ClaimRules.GroupClaimOpen(snapshot, entry)));
        Diag($"{Label}: claiming {plot} as {entry.Buyer} would {effect}{(effect == ClaimEffect.Relocate ? $" the estate now at {DescribeEstate(context)}" : string.Empty)}; {refusal}.");
        if (refusal != ClaimRefusal.None)
        {
            await CloseResult(HousingDialogs.ReadResultDialog());
            End(SettlementResult.NeedsPlayer, SettlementSteps.FromClaim(refusal));
            return;
        }

        if (entry.Deposit is not { } deposit)
        {
            await CloseResult(HousingDialogs.ReadResultDialog());
            End(SettlementResult.NeedsPlayer, SettlementFailure.DepositUnknown);
            return;
        }

        if (!MayPress(ActionKind.Claim))
        {
            await CloseResult(HousingDialogs.ReadResultDialog());
            RunLog.Info($"{Label}: DRY RUN. {plot} would have been claimed ({effect}) with Yes on Addon row {HousingTextRows.Of(row).RowId}; the dialog was closed instead and nothing was pressed.");
            End(SettlementResult.DryRun);
            return;
        }

        var outcome = await Submit(journal, ActionKind.Claim, row, deposit);
        if (outcome is not (SubmissionOutcome.Dispatched or SubmissionOutcome.DispatchUnrecorded))
        {
            await EndUnpressed(outcome);
            return;
        }

        await Advance(journal, entry.ReceiptId, SettlementState.ClaimPending, null);
        await AwaitClaim(journal);
    }

    // A claim of this plugin still in question is read back from the estate list alone; nothing is pressed.
    private async Task ReadBackClaim(EntryReceipt entry)
    {
        var journal = services.Housing.Journal!;
        var open = SettlementLookups.OpenAttempt(journal.Snapshot(), entry.ReceiptId)!;
        var owned = OwnsPlotNow();
        RunLog.Info($"{Label}: read-back of claim attempt {open.AttemptId} for {plot}: {(owned ? "the estate list holds it" : "the estate list does not hold it")}.");
        if (!owned)
        {
            End(SettlementResult.NeedsReconciliation, SettlementFailure.AttemptInQuestion);
            return;
        }

        var confirmed = await Write(() => ConfirmAttempt(journal, open.AttemptId, null), $"claim attempt {open.AttemptId} confirmed");
        var claimed = (confirmed is JournalWrite.Written or JournalWrite.AlreadyRecorded) && await Advance(journal, entry.ReceiptId, SettlementState.Claimed, null);
        Publish(Progress with { Outcome = claimed ? InspectionOutcome.Claimed : null });
        End(claimed ? SettlementResult.Claimed : SettlementResult.NeedsPlayer, claimed ? SettlementFailure.None : SettlementFailure.JournalUnavailable);
    }

    // The only press: the click window must be clear before the intent is written, so Yes is never refused for the
    // throttle once the intent is on disk, and it is never retried.
    private async Task<SubmissionOutcome> Submit(ActionJournal journal, ActionKind kind, HousingText row, long amount)
    {
        if (!await WaitUntilTimed(HousingDialogs.ClickReady, ClickWindowWaitMs, "settlement-yes-window", ObservePollFrames)
            || CharacterContextReader.ReadGil() is not { } gilBefore)
        {
            return SubmissionOutcome.PressRefused;
        }

        var entry = receipt!;
        var intent = new ActionAttempt
        {
            AttemptId = Guid.NewGuid(),
            Kind = kind,
            RunId = visitId,
            CharacterContentId = entry.CharacterContentId,
            HomeWorldId = entry.HomeWorldId,
            Cycle = entry.Cycle,
            Plot = plot,
            Buyer = entry.Buyer,
            Amount = amount,
            PreparedUtc = DateTime.UtcNow,
            GilBefore = gilBefore,
            ReceiptId = entry.ReceiptId,
        };
        ownAttempt = intent;
        Move(SettlementStep.PersistIntent);
        var outcome = await new EntrySubmission(journal).Submit(intent, MayPress(kind), ReturnToGame, () => PressYes(kind, row, amount), static () => DateTime.UtcNow);
        attemptSettled = outcome is not (SubmissionOutcome.Dispatched or SubmissionOutcome.DispatchUnrecorded);
        RunLog.Info($"{Label}: {kind} attempt {intent.AttemptId} for {plot}, {amount} gil, cycle {entry.Cycle}: {outcome}.");
        return outcome;
    }

    private async Task ReturnToGame() => await NextFrame();

    // The dialog and the character are read once more on this frame: still this character, still exactly the expected
    // row and, for a refund, the same amount. The press counts as made from the moment it is sent, so a fault after it can
    // never release the intent; the grant is used up only by a press that was sent.
    private DialogActionResult PressYes(ActionKind kind, HousingText row, long amount)
    {
        Move(SettlementStep.Press);
        if (Svc.PlayerState.ContentId != Progress.ContentId
            || HousingDialogs.ReadResultDialog() is not { Row: { } shown } reading
            || shown != row
            || (kind == ActionKind.Refund && reading.Refund != amount)
            || !MayPress(kind))
        {
            return DialogActionResult.IdentityMismatch;
        }

        pressed = true;
        DialogActionResult result;
        using (NativeActionGate.Allow(NativeAction.AcceptResult))
        {
            result = HousingDialogs.AcceptResult(row);
        }

        if (result != DialogActionResult.Pressed)
        {
            pressed = false;
            return result;
        }

        services.ConsumeGrant(kind);
        return result;
    }

    private async Task EndUnpressed(SubmissionOutcome outcome)
    {
        await CloseResult(HousingDialogs.ReadResultDialog());
        End(SettlementResult.NeedsPlayer, outcome switch
        {
            SubmissionOutcome.SlotTaken or SubmissionOutcome.IntentRefused => SettlementFailure.AttemptInQuestion,
            SubmissionOutcome.JournalFault => SettlementFailure.JournalUnavailable,
            _ => SettlementFailure.PressRefused,
        });
    }

    // After Yes on a refund: the gil is watched, a result dialog still open is closed and never answered again, and the
    // same placard is read again; its View Lottery Results label matched earlier in this visit, so its absence now means
    // the refund went through. A re-read that has not caught up is taken once more a minute later.
    private async Task AwaitRefund(ActionJournal journal, long offered)
    {
        Move(SettlementStep.AwaitSettlement);
        services.Observers.HousingLog.BeginCapture();
        var gilBefore = ownAttempt!.GilBefore!.Value;
        await WaitUntilTimed(() => CharacterContextReader.ReadGil() is { } gil && gil - gilBefore == offered, SettlementGilWaitMs, "settlement-refund-gil", ObservePollFrames);
        var dialogGone = await CloseLeftoverDialog();
        var evidence = new RefundEvidence(offered, gilBefore, CharacterContextReader.ReadGil(), dialogGone, await PlacardStillOffers());
        if (RefundRules.NeedsSecondLook(evidence))
        {
            Diag($"{Label}: the gil rose but the placard still offers the result or could not be read; reading it again in {SecondLookDelayMs / TimeUnits.MillisecondsPerSecond}s.");
            await DelayMs(SecondLookDelayMs);
            evidence = evidence with { GilAfter = CharacterContextReader.ReadGil(), PlacardStillOffers = await PlacardStillOffers(), Looks = evidence.Looks + 1 };
        }

        HousingDialogs.ClosePlacard();
        services.Observers.HousingLog.EndCapture();
        var settled = RefundRules.Settle(evidence);
        RunLog.Info($"{Label}: refund for attempt {ownAttempt.AttemptId}: {settled.Verdict} (gil {gilBefore} -> {evidence.GilAfter?.ToString() ?? "unknown"}, offered {offered}, "
                  + $"dialog {(evidence.DialogGone ? "gone" : "still open")}, placard {(evidence.PlacardStillOffers is { } still ? still ? "still offers the result" : "offers no result" : "not re-read")}, "
                  + $"looks {evidence.Looks}).");
        Publish(Progress with { Received = settled.Received });
        if (settled.Verdict == RefundVerdict.Uncertain)
        {
            await LeaveForReconciliation(journal);
            return;
        }

        var confirmed = await Write(() => ConfirmAttempt(journal, ownAttempt.AttemptId, null), $"refund attempt {ownAttempt.AttemptId} confirmed");
        var refunded = (confirmed is JournalWrite.Written or JournalWrite.AlreadyRecorded) && await Advance(journal, receipt!.ReceiptId, SettlementState.Refunded, settled.Received);
        attemptSettled = refunded;
        if (!refunded)
        {
            await LeaveForReconciliation(journal);
            return;
        }

        End(settled.Verdict == RefundVerdict.Refunded ? SettlementResult.Refunded : SettlementResult.RefundedAmountDiffers);
    }

    // The same plot's placard opened again and read once its sale data has arrived: whether it still offers View Lottery
    // Results to this character, null when it could not be opened and confirmed.
    private async Task<bool?> PlacardStillOffers()
    {
        HousingDialogs.ClosePlacard();
        await WaitUntilTimed(() => !AddonReads.IsReady(Game.Placard.PlacardReader.AddonName), PlacardCloseWaitMs, "settlement-placard-close");
        var destination = HousingDestination.Of(plot);
        if (await OpenPlacardAt(destination, services.Observers, manualArrival: false, Label) != VisitFailure.None)
        {
            return null;
        }

        var (placard, _) = await ReadPlacardSettled(services.Observers, services.Housing);
        if (placard is not { } read || PlacardIdentity.Verify(destination, read.Address) != PlacardIdentityVerdict.Confirmed || read.Sale is null)
        {
            return null;
        }

        return ResultsOffered(read);
    }

    private async Task AwaitClaim(ActionJournal journal)
    {
        Move(SettlementStep.AwaitSettlement);
        services.Observers.HousingLog.BeginCapture();
        await ConfirmClaimFromEstates(journal);
        services.Observers.HousingLog.EndCapture();
    }

    // The claim counts only when the character's estate list holds the plot, looked at again a minute later before it is
    // left in question; a dialog that merely vanished proves nothing.
    private async Task ConfirmClaimFromEstates(ActionJournal journal)
    {
        var entry = receipt!;
        var owned = await WaitUntilTimed(OwnsPlotNow, OwnershipWaitMs, "settlement-claim-estate", ObservePollFrames);
        if (!owned)
        {
            await CloseLeftoverDialog();
            HousingDialogs.ClosePlacard();
            Diag($"{Label}: {plot} is not in the estate list yet; looking again in {SecondLookDelayMs / TimeUnits.MillisecondsPerSecond}s.");
            await DelayMs(SecondLookDelayMs);
            owned = OwnsPlotNow();
        }

        var context = CharacterContextReader.Read();
        RunLog.Info($"{Label}: claim of {plot}: {(owned ? "the estate list holds it" : "the estate list does not hold it")} ({DescribeEstate(context)}).");
        if (!owned)
        {
            await LeaveForReconciliation(journal);
            return;
        }

        var confirmed = await Write(() => ConfirmAttempt(journal, ownAttempt!.AttemptId, null), $"claim attempt {ownAttempt!.AttemptId} confirmed");
        var claimed = (confirmed is JournalWrite.Written or JournalWrite.AlreadyRecorded) && await Advance(journal, entry.ReceiptId, SettlementState.Claimed, null);
        attemptSettled = claimed;
        if (!claimed)
        {
            await LeaveForReconciliation(journal);
            return;
        }

        Publish(Progress with { Outcome = InspectionOutcome.Claimed });
        End(SettlementResult.Claimed);
    }

    private bool OwnsPlotNow()
        => CharacterContextReader.Read() is { } context && ClaimRules.Owns(receipt!.Buyer, context.PersonalEstate, context.FreeCompanyEstate, plot);

    private async Task LeaveForReconciliation(ActionJournal journal)
    {
        var attempt = ownAttempt!;
        await Write(() => journal.RequireReconciliation(attempt.AttemptId, DateTime.UtcNow), $"{attempt.Kind} attempt {attempt.AttemptId} left for reconciliation");
        await Advance(journal, receipt!.ReceiptId, SettlementState.NeedsReconciliation, null);
        attemptSettled = true;
        End(SettlementResult.NeedsReconciliation);
    }

    // Whatever the press left open: a result dialog still showing is closed, never answered again; any other Yes/No
    // window is left alone. True when no result dialog is open any more.
    private async Task<bool> CloseLeftoverDialog()
    {
        if (HousingDialogs.IdentifyResultDialog() is { Text: not null } still)
        {
            Warn($"{Label}: the result dialog ({still.Text}) is still open after Yes; it is closed, never pressed again.");
            await CloseResult(HousingDialogs.ReadResultDialog());
            return false;
        }

        if (HousingDialogs.IdentifyResultDialog() is { } other)
        {
            Warn($"{Label}: a Yes/No window that is no lottery result is open ('{other.ShownText}'); it is left alone.");
        }

        return true;
    }

    private static string DescribeEstate(CharacterContext? context)
        => context is null
            ? "no character"
            : $"personal estate {context.PersonalEstate?.ToString() ?? "none"}, free company estate {context.FreeCompanyEstate?.ToString() ?? "none"}";
}
