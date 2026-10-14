using System.Threading.Tasks;
using AutoHouseLottery.Core.External;
using AutoHouseLottery.Core.Game;
using AutoHouseLottery.Core.Game.Character;
using AutoHouseLottery.Core.Game.Dialogs;
using AutoHouseLottery.Core.Game.Lottery;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Housing.Model;
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

    // Reads the result at the open placard, records what its row says, then hands a due refund or claim to the press or
    // closes the dialog. Inspection itself never presses Yes.
    private async Task InspectAndAct()
    {
        var (placard, _) = await ReadPlacardSettled(services.Observers, services.Housing);
        var identity = PlacardIdentity.Verify(HousingDestination.Of(plot), placard?.Address);
        var offered = placard is { } shownPlacard && ResultsOffered(shownPlacard);
        var evidence = placard is { } read ? EvidenceOf(read) : default;
        Move(SettlementStep.ViewResult);
        if (AutoAnswerGuard.Answering)
        {
            End(SettlementResult.NeedsPlayer, SettlementFailure.AutoAnswerPlugin);
            return;
        }

        ResultDialogReading? dialog = identity == PlacardIdentityVerdict.Confirmed && offered ? await ViewResult() : null;
        var kind = ResultKinds.Of(dialog is not null, dialog?.Row);
        Diag($"{Label}: placard for {plot}: identity {identity} ({placard?.Agreement.ToString() ?? "no reading"}), housing {placard?.HousingType?.ToString() ?? "unknown"}, "
           + $"sale {(evidence.SaleRead ? "read" : "not read")}, buttons {(evidence.AllLabelsKnown ? "all known" : "not all known")}, View Lottery Results {(offered ? "offered" : "not offered")}; "
           + $"result {kind}{(dialog is { } shown ? $" ('{shown.ShownText}', refund {shown.Refund?.ToString() ?? "none read"}, deadline {shown.RefundDeadline?.ToString("u") ?? "none read"})" : string.Empty)}.");
        var position = services.Housing.CurrentPosition(DateTime.UtcNow);
        var cycle = receipt?.Cycle ?? uncertainEntry!.Cycle;
        var currentCycle = cycle == position.Cycle && position.Phase == LotteryPhase.Results;
        var noResultOffered = dialog is null && identity == PlacardIdentityVerdict.Confirmed && !offered;
        var readingShowsNoEntry = false;
        if (noResultOffered)
        {
            HousingDialogs.ClosePlacard();
            readingShowsNoEntry = currentCycle && await ReadingShowsNoEntry();
        }

        var context = CharacterContextReader.Read();
        var buyer = receipt?.Buyer ?? uncertainEntry?.Buyer ?? BuyerType.Unknown;
        var ownsPlot = context is { } character && ClaimRules.Owns(buyer, character.PersonalEstate, character.FreeCompanyEstate, plot);
        var claimBy = SettlementDeadlines.ResultsEnd(cycle, position, services.Housing.Schedule);
        var absence = new AbsenceEvidence(
            evidence,
            currentCycle,
            readingShowsNoEntry,
            SeenInAnotherSession(request.Id, evidence.Strong),
            claimBy is { } end && end <= DateTime.UtcNow);
        RememberAbsence(request.Id, noResultOffered, evidence.Strong, kind);
        Move(SettlementStep.Classify);
        if (uncertainEntry is { } attempt && !await SettleUncertainEntry(attempt, identity, offered, kind, ownsPlot, absence))
        {
            return;
        }

        var journal = services.Housing.Journal!;
        var entry = receipt!;
        var snapshot = journal.Snapshot();
        var recorded = SettlementLookups.Settlement(snapshot, entry.ReceiptId)!.Value;
        var open = SettlementLookups.OpenAttempt(snapshot, entry.ReceiptId);
        var refundDeadline = recorded.RefundDeadlineUtc ?? SettlementDeadlines.RefundDeadline(entry.Cycle, position, services.Housing.Schedule);
        var facts = new InspectionFacts(identity, offered, kind, dialog?.Refund, recorded.State, recorded.Refund, open?.Kind, ownsPlot,
            refundDeadline is { } deadline && deadline < DateTime.UtcNow, absence);
        var verdict = InspectionRules.Decide(facts);
        Publish(Progress with { Outcome = verdict.Outcome, OfferedRefund = verdict.Refund });
        Diag($"{Label}: receipt {entry.ReceiptId} was {recorded.State}{(open is { } held ? $" with a {held.Kind} attempt {held.AttemptId} open" : string.Empty)}; "
           + $"the visit reads {verdict.Outcome}{(verdict.Reason == InspectionReason.None ? string.Empty : $" ({verdict.Reason})")}{(verdict.ByAbsence ? " from a placard without a result" : string.Empty)}.");
        if (verdict.Outcome == InspectionOutcome.OutcomeUnknown)
        {
            await CloseResult(dialog);
            if (identity == PlacardIdentityVerdict.Confirmed && SettlementStates.OutcomeOpen(recorded.State) && recorded.State != SettlementState.OutcomeUnknown)
            {
                await Advance(journal, entry.ReceiptId, SettlementState.OutcomeUnknown, null);
            }

            End(SettlementResult.NeedsPlayer, SettlementSteps.FromInspection(verdict.Reason));
            return;
        }

        var target = InspectionRules.StateOf(verdict.Outcome);
        if (SettlementPath.Plan(recorded.State, target, new SettlementState[SettlementPath.MaximumSteps]) == SettlementPath.Unreachable)
        {
            Warn($"{Label}: receipt {entry.ReceiptId} cannot go from {recorded.State} to {target}; nothing is recorded.");
            await CloseResult(dialog);
            End(SettlementResult.NeedsPlayer, SettlementFailure.ContradictsRecord);
            return;
        }

        var recordFailure = await Record(journal, entry, verdict, kind, ownsPlot, dialog, open);
        if (recordFailure != SettlementFailure.None)
        {
            await CloseResult(dialog);
            End(SettlementResult.NeedsPlayer, recordFailure);
            return;
        }

        switch (SettlementDecisions.Act(request.Mode, kind, verdict, open is not null))
        {
            case VisitAction.CollectRefund:
                await CollectRefund(journal, kind, ResultKinds.AcceptRow(kind, request.Mode)!.Value, verdict.Refund!.Value);
                return;
            case VisitAction.Claim:
                await Claim(journal, ResultKinds.AcceptRow(kind, request.Mode)!.Value);
                return;
            default:
                await CloseResult(dialog);
                End(SettlementResult.Classified);
                return;
        }
    }

    // Writes what the visit established: the plot an imported entry named, once a result proved it; the own attempt it
    // settles; the outcome; and the refund deadline the dialog named.
    private async Task<SettlementFailure> Record(ActionJournal journal, EntryReceipt entry, InspectionVerdict verdict, ResultKind kind, bool ownsPlot, ResultDialogReading? dialog, ActionAttempt? open)
    {
        if (entry.Plot is null)
        {
            if (!SettlementDecisions.ProvesPlot(kind, ownsPlot))
            {
                return SettlementFailure.PlotUnknown;
            }

            var resolved = await Write(() => journal.ResolvePlot(entry.ReceiptId, plot), $"plot {plot} of the imported receipt {entry.ReceiptId}");
            if (resolved is not (JournalWrite.Written or JournalWrite.AlreadyRecorded))
            {
                return SettlementFailure.JournalUnavailable;
            }

            receipt = entry with { Plot = plot };
        }

        if (open is { } attempt && SettlementDecisions.ResolveOpenAttempt(attempt.Kind, verdict.Outcome) is var resolution and not AttemptResolution.Keep)
        {
            var confirm = resolution == AttemptResolution.Confirm;
            var written = await Write(() => confirm ? ConfirmAttempt(journal, attempt.AttemptId, null) : journal.Reject(attempt.AttemptId, DateTime.UtcNow),
                $"{attempt.Kind} attempt {attempt.AttemptId} {(confirm ? "confirmed" : "released")}");
            if (written is not (JournalWrite.Written or JournalWrite.AlreadyRecorded))
            {
                return SettlementFailure.JournalUnavailable;
            }
        }

        var target = InspectionRules.StateOf(verdict.Outcome);
        var owed = target is SettlementState.LostRefundDue or SettlementState.ForfeitRefundDue;
        if (!await Advance(journal, entry.ReceiptId, target, owed ? verdict.Refund : null, verdict.ByAbsence))
        {
            return SettlementFailure.JournalUnavailable;
        }

        if (owed && dialog?.RefundDeadline is { } shownDeadline && ToUtc(shownDeadline) is { } deadlineUtc)
        {
            var estimate = SettlementDeadlines.RefundDeadline(entry.Cycle, services.Housing.CurrentPosition(DateTime.UtcNow), services.Housing.Schedule);
            if (SettlementDeadlines.PlausibleShownDeadline(deadlineUtc, estimate))
            {
                await Write(() => journal.RecordRefundDeadline(entry.ReceiptId, deadlineUtc, DateTime.UtcNow), $"refund deadline {deadlineUtc:u}");
            }
            else
            {
                Warn($"{Label}: the dialog's refund deadline {deadlineUtc:u} is far from the estimate {estimate?.ToString("u") ?? "unknown"}; the estimate is kept.");
            }
        }

        return SettlementFailure.None;
    }

    // An earlier cycle's entry attempt in question: a result at its plot proves the entry and makes it a receipt, which
    // the rest of the visit then settles. Only the absence rule can prove there was none.
    private async Task<bool> SettleUncertainEntry(ActionAttempt attempt, PlacardIdentityVerdict identity, bool offered, ResultKind kind, bool ownsPlot, AbsenceEvidence absence)
    {
        var verdict = InspectionRules.DecideAttempt(identity, offered, kind, ownsPlot, absence);
        Diag($"{Label}: the uncertain entry attempt {attempt.AttemptId} for {attempt.Plot} in cycle {attempt.Cycle} reads as {verdict}.");
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
                var confirmed = await Write(() => ConfirmAttempt(journal, attempt.AttemptId, entered), $"entry attempt {attempt.AttemptId} confirmed as receipt {entered.ReceiptId}");
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
                var released = rejected is JournalWrite.Written or JournalWrite.AlreadyRecorded;
                End(released ? SettlementResult.Classified : SettlementResult.NeedsPlayer, released ? SettlementFailure.None : SettlementFailure.JournalUnavailable);
                return false;
            default:
                await CloseResult(HousingDialogs.ReadResultDialog());
                End(SettlementResult.NeedsPlayer, identity != PlacardIdentityVerdict.Confirmed ? SettlementFailure.PlacardUnverified
                    : offered ? SettlementFailure.ResultNotShown : SettlementFailure.NoResultOffered);
                return false;
        }
    }

    // A placard without a result is remembered with the session it was seen in; a result row seen later forgets it.
    private void RememberAbsence(Guid id, bool noResultOffered, bool strong, ResultKind kind)
    {
        if (noResultOffered)
        {
            services.Configuration.RecordAbsenceSighting(id, services.SessionId(), strong, DateTime.UtcNow);
            return;
        }

        if (kind != ResultKind.NotShown)
        {
            services.Configuration.ForgetAbsenceSighting(id);
        }
    }

    private bool SeenInAnotherSession(Guid id, bool strongNow)
        => strongNow
        && services.Configuration.AbsenceSighting(id) is { } earlier
        && AbsenceRules.CountsAsSecondSighting(earlier.Strong, earlier.Session, earlier.AtUtc, services.SessionId(), DateTime.UtcNow);

    private static PlacardEvidence EvidenceOf(PlacardReading placard)
    {
        var allKnown = true;
        var lotteryButton = false;
        var buttons = placard.Buttons;
        for (var buttonIndex = 0; buttonIndex < buttons.Length; buttonIndex++)
        {
            var button = buttons[buttonIndex];
            if (!button.Visible || button.Text.Length == 0)
            {
                continue;
            }

            allKnown &= button.MatchedText is not null;
            lotteryButton |= button.MatchedText is HousingText.EnterLotteryButton or HousingText.ViewResultsButton;
        }

        return new PlacardEvidence(placard.Sale is not null, placard.HousingType == PlacardHousingType.UnownedHouse, allKnown, lotteryButton);
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
        var pressedView = DialogActionResult.Throttled;
        for (var attempt = 1; attempt <= ViewResultsPressAttempts && pressedView == DialogActionResult.Throttled; attempt++)
        {
            await WaitUntilTimed(HousingDialogs.ClickReady, ClickWindowWaitMs, "settlement-view-results-window", ObservePollFrames);
            using (NativeActionGate.Allow(NativeAction.PressViewResults))
            {
                pressedView = HousingDialogs.PressPlacardButton(HousingText.ViewResultsButton);
            }
        }

        if (pressedView != DialogActionResult.Pressed)
        {
            Diag($"{Label}: View Lottery Results could not be pressed ({pressedView}).");
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

    // Whether a fresh reading of the validated lottery record shows no entry at all. The Timers text never can.
    private async Task<bool> ReadingShowsNoEntry()
    {
        if (CharacterContextReader.Read() is not { OnHomeWorld: true } context)
        {
            return false;
        }

        var pass = new LotteryTimersPass();
        await RunCancellable(pass, TimersPassWatchdogMs, "settlement-lottery-reading");
        CancelToken.ThrowIfCancellationRequested();
        Diag($"{Label}: lottery reading {pass.Outcome}; {LotteryReader.Describe(pass.Snapshot)}.");
        if (pass.Outcome != LotteryPassOutcome.Loaded || pass.Snapshot.ContentId != context.ContentId)
        {
            return false;
        }

        await services.Housing.ImportFromReading(pass.Snapshot, context.ContentId, context.HomeWorldId);
        await NextFrame();
        return pass.Snapshot is { State: LotterySnapshotState.LoadedNone, Source: LotterySource.Record };
    }

    // The close-based Cancel, with its own throttle, retried until the dialog is gone or the wait ends.
    private async Task<bool> CloseResult(ResultDialogReading? dialog)
    {
        if (dialog is null && !HousingDialogs.ResultDialogOpen())
        {
            return true;
        }

        var deadline = Environment.TickCount64 + CloseWaitMs;
        while (HousingDialogs.ResultDialogOpen() && Environment.TickCount64 < deadline)
        {
            CloseResultNow();
            await NextFrame(ObservePollFrames);
        }

        var closed = !HousingDialogs.ResultDialogOpen();
        resultOpenedByVisit &= !closed;
        Diag($"{Label}: the result dialog {(closed ? "is closed" : "did not close")}; nothing on it was pressed.");
        return closed;
    }

    private static DateTime? ToUtc(DateTime shownLocal)
    {
        try
        {
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(shownLocal, DateTimeKind.Unspecified), TimeZoneInfo.Local);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
