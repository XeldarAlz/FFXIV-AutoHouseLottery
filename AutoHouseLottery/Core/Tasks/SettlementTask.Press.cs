using System.Threading.Tasks;
using AutoHouseLottery.Core.Game;
using AutoHouseLottery.Core.Game.Character;
using AutoHouseLottery.Core.Game.Dialogs;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Persistence;
using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Settlement.Model;
using AutoHouseLottery.Core.Tasks.Model;
using ECommons.DalamudServices;

namespace AutoHouseLottery.Core.Tasks;

internal sealed partial class SettlementTask
{
    private const int SettlementGilWaitMs = 30_000;
    private const int OwnershipWaitMs = 30_000;
    private const int SecondLookDelayMs = 60_000;

    private long dispatchSequence;

    // A refund the dialog offers on row 7124 or 7123: the gate, the durable intent, one Yes, then the gil and fresh
    // readings. An offer the recorded deposit does not explain is left to the player.
    private async Task CollectRefund(ActionJournal journal, ResultKind kind, HousingText row, long offered)
    {
        var entry = receipt!;
        var check = RefundRules.CheckOffer(ResultKinds.RefundOf(kind), offered, entry.Deposit);
        Diag($"Settlement {visitId}: the game offers a {kind} refund of {offered} gil for a deposit of {entry.Deposit?.ToString() ?? "unknown"} gil; {check}.");
        if (check == RefundOffer.DiffersFromDeposit)
        {
            await CloseResult(HousingDialogs.ReadResultDialog());
            End(SettlementResult.NeedsPlayer, SettlementFailure.RefundDiffersFromDeposit);
            return;
        }

        if (!MayPress(ActionKind.Refund))
        {
            await CloseResult(HousingDialogs.ReadResultDialog());
            RunLog.Info($"Settlement {visitId}: DRY RUN. The refund of {offered} gil for {plot} would have been collected with Yes on Addon row {HousingTextRows.Of(row).RowId}; "
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

    // A win on row 7122 or 7128, only for the player's own click: the claim rules, the gate, the durable intent, one Yes,
    // then the character's estate list. Nothing that follows Yes is pressed.
    private async Task Claim(ActionJournal journal, HousingText row)
    {
        var entry = receipt!;
        var snapshot = journal.Snapshot();
        var context = CharacterContextReader.Read();
        var effect = context is { } shown ? ClaimRules.EffectOf(entry.Buyer, shown.FreeCompanyId is not null, shown.PersonalEstate, shown.FreeCompanyEstate) : ClaimEffect.Unknown;
        Publish(Progress with { Effect = effect });
        var refusal = context is null
            ? ClaimRefusal.NotTheEntrant
            : ClaimRules.Check(new ClaimFacts(context.ContentId, entry, SettlementLookups.Settlement(snapshot, entry.ReceiptId)!.Value.State, context.FreeCompanyId is not null,
                context.PersonalEstate, context.FreeCompanyEstate, ClaimRules.GroupClaimOpen(snapshot, entry)));
        Diag($"Settlement {visitId}: claiming {plot} as {entry.Buyer} would {effect}{(effect == ClaimEffect.Relocate ? $" the estate now at {DescribeEstate(context)}" : string.Empty)}; {refusal}.");
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
            RunLog.Info($"Settlement {visitId}: DRY RUN. {plot} would have been claimed ({effect}) with Yes on Addon row {HousingTextRows.Of(row).RowId}; the dialog was closed instead and nothing was pressed.");
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
        RunLog.Info($"Settlement {visitId}: read-back of claim attempt {open.AttemptId} for {plot}: {(owned ? "the estate list holds it" : "the estate list does not hold it")}.");
        if (!owned)
        {
            End(SettlementResult.NeedsReconciliation, SettlementFailure.AttemptInQuestion);
            return;
        }

        var confirmed = await Write(() => Resolve(journal, open, wentThrough: true), $"claim attempt {open.AttemptId} confirmed");
        var claimed = (confirmed is JournalWrite.Written or JournalWrite.AlreadyRecorded) && await Advance(journal, entry.ReceiptId, SettlementState.Claimed, null);
        Publish(Progress with { Outcome = claimed ? InspectionOutcome.Claimed : null });
        End(claimed ? SettlementResult.Claimed : SettlementResult.NeedsPlayer, claimed ? SettlementFailure.None : SettlementFailure.JournalUnavailable);
    }

    private bool MayPress(ActionKind kind)
        => Svc.PlayerState.ContentId == Progress.ContentId
        && SettlementGate.MayPress(kind, SettlementGate.Validated(kind), request.DryRun, services.Grant(kind), Progress.ContentId, plot);

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
        RunLog.Info($"Settlement {visitId}: {kind} attempt {intent.AttemptId} for {plot}, {amount} gil, cycle {entry.Cycle}: {outcome}.");
        return outcome;
    }

    private async Task ReturnToGame() => await NextFrame();

    // The dialog and the character are read once more on this frame: still this character, still the expected row and,
    // for a refund, the same amount. The grant is used up only by a press that was sent.
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

        dispatchSequence = GameSequence.Next();
        DialogActionResult result;
        using (NativeActionGate.Allow(NativeAction.AcceptResult))
        {
            result = HousingDialogs.AcceptResult(row);
        }

        if (result == DialogActionResult.Pressed)
        {
            pressed = true;
            services.ConsumeGrant(kind);
        }

        return result;
    }

    private async Task EndUnpressed(SubmissionOutcome outcome)
    {
        await CloseResult(HousingDialogs.ReadResultDialog());
        End(SettlementResult.NeedsPlayer, outcome switch
        {
            SubmissionOutcome.GateClosed => SettlementFailure.PressRefused,
            SubmissionOutcome.SlotTaken or SubmissionOutcome.IntentRefused => SettlementFailure.AttemptInQuestion,
            SubmissionOutcome.JournalFault => SettlementFailure.JournalUnavailable,
            _ => SettlementFailure.PressRefused,
        });
    }

    // After Yes on a refund: the gil is watched, leftover windows are closed, and fresh readings decide; a reading that
    // has not caught up is taken again a minute later. Nothing here presses anything again.
    private async Task AwaitRefund(ActionJournal journal, long offered)
    {
        Move(SettlementStep.AwaitSettlement);
        services.Observers.HousingLog.BeginCapture();
        var gilBefore = ownAttempt!.GilBefore!.Value;
        await WaitUntilTimed(() => CharacterContextReader.ReadGil() is { } gil && gil - gilBefore == offered, SettlementGilWaitMs, "settlement-refund-gil", ObservePollFrames);
        await CloseLeftovers();
        var context = CharacterContextReader.Read();
        var first = context is { OnHomeWorld: true } here && here.ContentId == Progress.ContentId ? await RefreshLottery(here) : null;
        var evidence = new RefundEvidence(offered, gilBefore, CharacterContextReader.ReadGil(), first is { } reading ? EntryShown(reading, plot) : null);
        if (RefundRules.NeedsSecondReading(evidence))
        {
            Diag($"Settlement {visitId}: the gil rose but the first reading still shows the entry or cannot say; reading again in {SecondLookDelayMs / TimeUnits.MillisecondsPerSecond}s.");
            await DelayMs(SecondLookDelayMs);
            var again = CharacterContextReader.Read() is { OnHomeWorld: true } later && later.ContentId == Progress.ContentId ? await RefreshLottery(later) : null;
            evidence = evidence with { GilAfter = CharacterContextReader.ReadGil(), SecondReadingTaken = true, SecondEntryShown = again is { } second ? EntryShown(second, plot) : null };
        }

        services.Observers.HousingLog.EndCapture();
        var settled = RefundRules.Settle(evidence);
        RunLog.Info($"Settlement {visitId}: refund for attempt {ownAttempt.AttemptId}: {settled.Verdict} (gil {gilBefore} -> {evidence.GilAfter?.ToString() ?? "unknown"}, offered {offered}, "
                  + $"entry shown {evidence.EntryShown?.ToString() ?? "unknown"}, second reading {(evidence.SecondReadingTaken ? evidence.SecondEntryShown?.ToString() ?? "unknown" : "not taken")}).");
        Publish(Progress with { Received = settled.Received });
        if (settled.Verdict == RefundVerdict.Uncertain)
        {
            await LeaveForReconciliation(journal);
            return;
        }

        var confirmed = await Write(() => Resolve(journal, ownAttempt, wentThrough: true), $"refund attempt {ownAttempt.AttemptId} confirmed");
        var refunded = (confirmed is JournalWrite.Written or JournalWrite.AlreadyRecorded) && await Advance(journal, receipt!.ReceiptId, SettlementState.Refunded, settled.Received);
        attemptSettled = refunded;
        if (!refunded)
        {
            await LeaveForReconciliation(journal);
            return;
        }

        End(settled.Verdict == RefundVerdict.Refunded ? SettlementResult.Refunded : SettlementResult.RefundedAmountDiffers);
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
            await CloseLeftovers();
            Diag($"Settlement {visitId}: {plot} is not in the estate list yet; looking again in {SecondLookDelayMs / TimeUnits.MillisecondsPerSecond}s.");
            await DelayMs(SecondLookDelayMs);
            owned = OwnsPlotNow();
        }

        var context = CharacterContextReader.Read();
        RunLog.Info($"Settlement {visitId}: claim of {plot}: {(owned ? "the estate list holds it" : "the estate list does not hold it")} ({DescribeEstate(context)}).");
        if (!owned)
        {
            await LeaveForReconciliation(journal);
            return;
        }

        var confirmed = await Write(() => Resolve(journal, ownAttempt!, wentThrough: true), $"claim attempt {ownAttempt!.AttemptId} confirmed");
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

    // Whatever the press left open: a result dialog still showing is closed, never answered again; the placard closed.
    private async Task CloseLeftovers()
    {
        if (HousingDialogs.IdentifyResultDialog() is { Text: not null } still)
        {
            Warn($"Settlement {visitId}: the result dialog ({still.Text}) is still open after Yes; it is closed, never pressed again.");
            await CloseResult(HousingDialogs.ReadResultDialog());
        }
        else if (HousingDialogs.IdentifyResultDialog() is { } other)
        {
            Warn($"Settlement {visitId}: a Yes/No window that is no lottery result is open ('{other.ShownText}'); it is left alone.");
        }

        HousingDialogs.ClosePlacard();
    }

    private static string DescribeEstate(CharacterContext? context)
        => context is null
            ? "no character"
            : $"personal estate {context.PersonalEstate?.ToString() ?? "none"}, free company estate {context.FreeCompanyEstate?.ToString() ?? "none"}";
}
