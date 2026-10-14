using System.Threading.Tasks;
using AutoHouseLottery.Core.Game;
using AutoHouseLottery.Core.Game.Character;
using AutoHouseLottery.Core.Game.Dialogs;
using AutoHouseLottery.Core.Game.Lottery;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Integration.Model;
using AutoHouseLottery.Core.Persistence;
using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Settlement.Model;
using AutoHouseLottery.Core.Travel.Model;
using LotteryPhase = AutoHouseLottery.Core.Housing.Model.LotteryPhase;

namespace AutoHouseLottery.Core.Tasks;

internal sealed partial class SettlementTask
{
    private const int ResultDialogWaitMs = 10_000;
    private const int ClickWindowWaitMs = 5_000;
    private const int ViewResultsPressAttempts = 3;
    private const int CloseWaitMs = 10_000;

    private static readonly Func<uint, string?> districtNameOf = DistrictNames.Of;

    // Reads the result at the open placard, records what its row says, then hands a due refund or claim to the press or
    // closes the dialog. Inspection itself never presses Yes.
    private async Task InspectAndAct()
    {
        var placard = await ReadPlacard(services.Observers);
        var identity = PlacardIdentity.Verify(DestinationOf(plot), placard?.Address);
        var offered = placard is { } reading && ResultsOffered(reading);
        Move(SettlementStep.ViewResult);
        ResultDialogReading? dialog = null;
        if (identity == PlacardIdentityVerdict.Confirmed && offered)
        {
            dialog = await ViewResult();
        }

        var kind = ResultKinds.Of(dialog is not null, dialog?.Row);
        Diag($"Settlement {visitId}: placard for {plot}: identity {identity} ({placard?.Agreement.ToString() ?? "no reading"}), View Lottery Results {(offered ? "offered" : "not offered")}; "
           + $"result {kind}{(dialog is { } shown ? $" ('{shown.ShownText}', refund {shown.Refund?.ToString() ?? "none read"})" : string.Empty)}.");
        bool? readingShowsEntry = null;
        if (dialog is null && identity == PlacardIdentityVerdict.Confirmed)
        {
            HousingDialogs.ClosePlacard();
            readingShowsEntry = await ReadEntryShown();
        }

        var context = CharacterContextReader.Read();
        var buyer = receipt?.Buyer ?? uncertainEntry?.Buyer ?? BuyerType.Unknown;
        var ownsPlot = context is { } character && ClaimRules.Owns(buyer, character.PersonalEstate, character.FreeCompanyEstate, plot);
        Move(SettlementStep.Classify);
        if (uncertainEntry is { } attempt && !await SettleUncertainEntry(attempt, identity, offered, kind, ownsPlot))
        {
            return;
        }

        var journal = services.Housing.Journal!;
        var entry = receipt!;
        var snapshot = journal.Snapshot();
        var recorded = SettlementLookups.Settlement(snapshot, entry.ReceiptId)!.Value;
        var open = SettlementLookups.OpenAttempt(snapshot, entry.ReceiptId);
        var position = services.Housing.CurrentPosition(DateTime.UtcNow);
        var refundDeadline = SettlementDeadlines.RefundDeadline(entry.Cycle, position, services.Housing.Schedule);
        var facts = new InspectionFacts(identity, offered, kind, dialog?.Refund, recorded.State, recorded.Refund, open?.Kind, readingShowsEntry, ownsPlot,
            refundDeadline is { } deadline && deadline < DateTime.UtcNow);
        var verdict = InspectionRules.Decide(facts);
        Publish(Progress with { Outcome = verdict.Outcome, OfferedRefund = verdict.Refund });
        Diag($"Settlement {visitId}: receipt {entry.ReceiptId} was {recorded.State}{(open is { } held ? $" with a {held.Kind} attempt {held.AttemptId} open" : string.Empty)}; "
           + $"the visit reads {verdict.Outcome}{(verdict.Reason == InspectionReason.None ? string.Empty : $" ({verdict.Reason})")}.");
        if (verdict.Outcome == InspectionOutcome.OutcomeUnknown)
        {
            await CloseResult(dialog);
            End(SettlementResult.NeedsPlayer, SettlementSteps.FromInspection(verdict.Reason));
            return;
        }

        var target = InspectionRules.StateOf(verdict.Outcome);
        if (SettlementPath.Plan(recorded.State, target, new SettlementState[MaximumPath]) == SettlementPath.Unreachable)
        {
            Warn($"Settlement {visitId}: receipt {entry.ReceiptId} cannot go from {recorded.State} to {target}; nothing is recorded.");
            await CloseResult(dialog);
            End(SettlementResult.NeedsPlayer, SettlementFailure.ContradictsRecord);
            return;
        }

        if (!await Record(journal, entry, verdict, dialog, open))
        {
            await CloseResult(dialog);
            End(SettlementResult.NeedsPlayer, SettlementFailure.JournalUnavailable);
            return;
        }

        await Act(journal, kind, verdict, dialog, open);
    }

    private async Task Act(ActionJournal journal, ResultKind kind, InspectionVerdict verdict, ResultDialogReading? dialog, ActionAttempt? openBeforeVisit)
    {
        var acceptRow = ResultKinds.AcceptRow(kind, request.Mode);
        if (dialog is null || acceptRow is not { } row || openBeforeVisit is not null)
        {
            await CloseResult(dialog);
            End(SettlementResult.Classified);
            return;
        }

        if (request.Mode == SettlementMode.Refund)
        {
            await CollectRefund(journal, kind, row, verdict.Refund!.Value);
            return;
        }

        await Claim(journal, row);
    }

    // Writes what the visit established: the plot an imported entry named, the own attempt it settles, and the outcome.
    private async Task<bool> Record(ActionJournal journal, EntryReceipt entry, InspectionVerdict verdict, ResultDialogReading? dialog, ActionAttempt? open)
    {
        if (entry.Plot is null)
        {
            var resolved = await Write(() => journal.ResolvePlot(entry.ReceiptId, plot), $"plot {plot} of the imported receipt {entry.ReceiptId}");
            if (resolved is not (JournalWrite.Written or JournalWrite.AlreadyRecorded))
            {
                return false;
            }

            receipt = entry with { Plot = plot };
        }

        if (open is { } attempt && !await ResolveOpenAttempt(journal, attempt, verdict.Outcome))
        {
            return false;
        }

        var target = InspectionRules.StateOf(verdict.Outcome);
        return await Advance(journal, entry.ReceiptId, target, target is SettlementState.LostRefundDue or SettlementState.ForfeitRefundDue ? verdict.Refund : null);
    }

    // The plugin's own refund or claim still in question is settled by what the plot shows now: the same result offered
    // again means the press never went through; the settled state means it did.
    private async Task<bool> ResolveOpenAttempt(ActionJournal journal, ActionAttempt attempt, InspectionOutcome outcome)
    {
        var wentThrough = (attempt.Kind == ActionKind.Refund && outcome == InspectionOutcome.Refunded) || (attempt.Kind == ActionKind.Claim && outcome == InspectionOutcome.Claimed);
        var notThrough = (attempt.Kind == ActionKind.Refund && outcome is InspectionOutcome.LostRefundDue or InspectionOutcome.ForfeitRefundDue)
            || (attempt.Kind == ActionKind.Claim && outcome == InspectionOutcome.WonUnclaimed);
        if (!wentThrough && !notThrough)
        {
            return true;
        }

        var written = await Write(() => Resolve(journal, attempt, wentThrough), $"{attempt.Kind} attempt {attempt.AttemptId} {(wentThrough ? "confirmed" : "released")}");
        return written is JournalWrite.Written or JournalWrite.AlreadyRecorded;
    }

    private static JournalWrite Resolve(ActionJournal journal, ActionAttempt attempt, bool wentThrough)
    {
        if (!wentThrough)
        {
            return journal.Reject(attempt.AttemptId, DateTime.UtcNow);
        }

        if (attempt.State == AttemptState.Prepared)
        {
            journal.RequireReconciliation(attempt.AttemptId, DateTime.UtcNow);
        }

        return journal.Confirm(attempt.AttemptId, null, DateTime.UtcNow);
    }

    // An earlier cycle's entry attempt in question: a result at its plot proves the entry and makes it a receipt, which
    // the rest of the visit then settles; a confirmed placard with no result proves there was none.
    private async Task<bool> SettleUncertainEntry(ActionAttempt attempt, PlacardIdentityVerdict identity, bool offered, ResultKind kind, bool ownsPlot)
    {
        var verdict = InspectionRules.DecideAttempt(identity, offered, kind, ownsPlot);
        Diag($"Settlement {visitId}: the uncertain entry attempt {attempt.AttemptId} for {attempt.Plot} in cycle {attempt.Cycle} reads as {verdict}.");
        var journal = services.Housing.Journal!;
        switch (verdict)
        {
            case AttemptVerdict.Entered:
                var entered = new EntryReceipt
                {
                    ReceiptId = Guid.NewGuid(),
                    CharacterContentId = attempt.CharacterContentId,
                    HomeWorldId = attempt.HomeWorldId,
                    Cycle = attempt.Cycle,
                    Plot = attempt.Plot,
                    Buyer = attempt.Buyer,
                    Evidence = ReceiptEvidence.Reconciled,
                    Deposit = attempt.Amount,
                };
                var confirmed = await Write(() => ConfirmEntry(journal, attempt, entered), $"entry attempt {attempt.AttemptId} confirmed as receipt {entered.ReceiptId}");
                if (confirmed is not (JournalWrite.Written or JournalWrite.AlreadyRecorded))
                {
                    await CloseResult(HousingDialogs.ReadResultDialog());
                    End(SettlementResult.NeedsPlayer, SettlementFailure.JournalUnavailable);
                    return false;
                }

                receipt = entered;
                uncertainEntry = null;
                Publish(Progress with { ReceiptId = entered.ReceiptId });
                if (Progress.TravelSpent > 0)
                {
                    var spent = Progress.TravelSpent;
                    await Write(() => journal.AddTravel(entered.ReceiptId, spent, DateTime.UtcNow), $"travel of {spent} gil");
                }

                return true;
            case AttemptVerdict.NotEntered:
                var rejected = await Write(() => journal.Reject(attempt.AttemptId, DateTime.UtcNow), $"entry attempt {attempt.AttemptId} released, its plot shows no result for this character");
                End(rejected is JournalWrite.Written or JournalWrite.AlreadyRecorded ? SettlementResult.Classified : SettlementResult.NeedsPlayer,
                    rejected is JournalWrite.Written or JournalWrite.AlreadyRecorded ? SettlementFailure.None : SettlementFailure.JournalUnavailable);
                return false;
            default:
                await CloseResult(HousingDialogs.ReadResultDialog());
                End(SettlementResult.NeedsPlayer, identity == PlacardIdentityVerdict.Confirmed ? SettlementFailure.ResultNotShown : SettlementFailure.PlacardUnverified);
                return false;
        }
    }

    private static JournalWrite ConfirmEntry(ActionJournal journal, ActionAttempt attempt, EntryReceipt entered)
    {
        if (attempt.State == AttemptState.Prepared)
        {
            journal.RequireReconciliation(attempt.AttemptId, DateTime.UtcNow);
        }

        return journal.Confirm(attempt.AttemptId, entered, DateTime.UtcNow);
    }

    private static bool ResultsOffered(PlacardReading placard)
    {
        var buttons = placard.Buttons;
        for (var buttonIndex = 0; buttonIndex < buttons.Length; buttonIndex++)
        {
            var button = buttons[buttonIndex];
            if (button.MatchedText == HousingText.ViewResultsButton && button.Enabled && button.Visible)
            {
                return true;
            }
        }

        return false;
    }

    // View Lottery Results (Addon row 7127), pressed by its own label once the shared click window is clear, then the
    // result dialog. Any Yes/No window that opens counts as shown; which result it is comes from its row alone.
    private async Task<ResultDialogReading?> ViewResult()
    {
        var pressed = DialogActionResult.Throttled;
        for (var attempt = 1; attempt <= ViewResultsPressAttempts && pressed == DialogActionResult.Throttled; attempt++)
        {
            await WaitUntilTimed(HousingDialogs.ClickReady, ClickWindowWaitMs, "settlement-view-results-window", ObservePollFrames);
            using (NativeActionGate.Allow(NativeAction.PressViewResults))
            {
                pressed = HousingDialogs.PressPlacardButton(HousingText.ViewResultsButton);
            }
        }

        if (pressed != DialogActionResult.Pressed)
        {
            Diag($"Settlement {visitId}: View Lottery Results could not be pressed ({pressed}).");
            return null;
        }

        services.Observers.HousingLog.BeginCapture();
        var shown = await WaitUntilTimed(() => HousingDialogs.IdentifyResultDialog() is not null, ResultDialogWaitMs, "settlement-result-dialog", ObservePollFrames);
        services.Observers.HousingLog.EndCapture();
        if (!shown)
        {
            return null;
        }

        resultOpenedByVisit = true;
        await NextFrame(ObservePollFrames);
        return HousingDialogs.ReadResultDialog();
    }

    // Whether a fresh lottery reading still shows this entry. Only the receipt's own results period can say anything;
    // an older cycle's entry may simply have left the record.
    private async Task<bool?> ReadEntryShown()
    {
        var position = services.Housing.CurrentPosition(DateTime.UtcNow);
        var cycle = receipt?.Cycle ?? uncertainEntry!.Cycle;
        if (cycle != position.Cycle || position.Phase != LotteryPhase.Results || CharacterContextReader.Read() is not { OnHomeWorld: true } context)
        {
            return null;
        }

        var reading = await RefreshLottery(context);
        return reading is { } fresh ? EntryShown(fresh, plot) : null;
    }

    private async Task<LotterySnapshot?> RefreshLottery(CharacterContext context)
    {
        var pass = new LotteryTimersPass();
        await RunCancellable(pass, TimersPassWatchdogMs, "settlement-lottery-reading");
        CancelToken.ThrowIfCancellationRequested();
        Diag($"Settlement {visitId}: lottery reading {pass.Outcome}; {LotteryReader.Describe(pass.Snapshot)}.");
        if (pass.Outcome != LotteryPassOutcome.Loaded || pass.Snapshot.ContentId != context.ContentId)
        {
            return null;
        }

        await services.Housing.ImportFromReading(pass.Snapshot, context.ContentId, context.HomeWorldId);
        await NextFrame();
        return pass.Snapshot;
    }

    private static bool? EntryShown(in LotterySnapshot reading, PlotKey plot) => reading.State switch
    {
        LotterySnapshotState.LoadedNone => false,
        LotterySnapshotState.Active when reading.Entry is { } entry => ReadingAddress.Names(entry, plot, districtNameOf),
        _ => null,
    };

    // The close-based Cancel, with its own throttle, retried until the dialog is gone or the wait ends.
    private async Task CloseResult(ResultDialogReading? dialog)
    {
        if (dialog is null && HousingDialogs.IdentifyResultDialog() is not { Text: not null })
        {
            return;
        }

        var deadline = Environment.TickCount64 + CloseWaitMs;
        while (HousingDialogs.IdentifyResultDialog() is { Text: not null } && Environment.TickCount64 < deadline)
        {
            CloseResultNow();
            await NextFrame(ObservePollFrames);
        }

        var closed = HousingDialogs.IdentifyResultDialog() is not { Text: not null };
        resultOpenedByVisit &= !closed;
        Diag($"Settlement {visitId}: the result dialog {(closed ? "is closed" : "did not close")}; nothing on it was pressed.");
    }
}
