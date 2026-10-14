using System.Threading.Tasks;
using AutoHouseLottery.Core.External;
using AutoHouseLottery.Core.Game;
using AutoHouseLottery.Core.Game.Character;
using AutoHouseLottery.Core.Game.Dialogs;
using AutoHouseLottery.Core.Game.Lottery;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Integration.Model;
using AutoHouseLottery.Core.Persistence;
using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Tasks.Model;
using ECommons.DalamudServices;

namespace AutoHouseLottery.Core.Tasks;

internal sealed partial class EntryRunTask
{
    private const int EnterPressAttempts = 3;
    private const int EnterPressGapMs = 700;
    private const int ConfirmationWaitMs = 30_000;
    private const int ConfirmationCloseWaitMs = 10_000;
    private const int CheckboxWaitMs = 10_000;
    private const int TickAttempts = 3;
    private const int ClickWindowWaitMs = 5_000;
    private const int ReceiptGilWaitMs = 30_000;
    private const int SecondReadingDelayMs = 60_000;

    private long dispatchSequence;
    private DateTime dispatchedUtc;

    // Opens the entry confirmation and checks it, then goes on to the intent.
    private async Task<EntryFailure> Enter()
    {
        Move(EntryStep.PrepareEntry);
        if (AutoAnswerGuard.Answering)
        {
            return EntryFailure.AutoAnswerPlugin;
        }

        var pressedAt = GameSequence.Next();
        if (!await PressEnterLottery())
        {
            return EntryFailure.EntryButtonUnavailable;
        }

        confirmationOpenedByRun = true;
        if (await AwaitConfirmation(pressedAt) is not { } shown)
        {
            if (rejection is not null)
            {
                return EntryFailure.Rejected;
            }

            return EntryFailure.ConfirmationNotShown;
        }

        session.ReadDeposit(shown.Deposit);
        var liveGil = CharacterContextReader.ReadGil();
        var expected = session.ExpectedDeposit(session.Target);
        var check = ConfirmationRules.Check(new ConfirmationFacts(shown.Identified, shown.Deposit, session.Mode, expected, session.Budget, OtherCommitments(), liveGil));
        Diag($"Entry run {session.RunId}: confirmation for {session.Target} shows {shown.Deposit?.ToString() ?? "no readable"} gil (expected {expected?.ToString() ?? "unknown"}), "
           + $"checkbox {shown.Proceed}, Confirm {(shown.ConfirmFound ? shown.ConfirmEnabled ? "enabled" : "disabled" : "missing")}, gil {liveGil?.ToString() ?? "unknown"}; {check}.");
        if (check != EntryFailure.None)
        {
            await CloseConfirmation();
            return check;
        }

        return await Submit(shown.Deposit!.Value);
    }

    private async Task<bool> PressEnterLottery()
    {
        for (var attempt = 1; attempt <= EnterPressAttempts; attempt++)
        {
            DialogActionResult result;
            using (NativeActionGate.Allow(NativeAction.PressEnterLottery))
            {
                result = HousingDialogs.PressPlacardButton(HousingText.EnterLotteryButton);
            }

            if (result == DialogActionResult.Pressed)
            {
                return true;
            }

            if (result != DialogActionResult.Throttled)
            {
                return false;
            }

            await DelayMs(EnterPressGapMs);
        }

        return false;
    }

    // Only a window whose text reads as Addon row 7046 counts; any other dialog is left alone. A refusal the game
    // logs after the press ends the wait, since no confirmation will come.
    private async Task<EntryConfirmationReading?> AwaitConfirmation(long pressedAt)
    {
        EntryConfirmationReading? shown = null;
        await WaitUntilTimed(() =>
        {
            if (services.Observers.HousingLog.FirstRejectionAfter(pressedAt) is { } refused)
            {
                rejection = refused.Text;
                return true;
            }

            shown = HousingDialogs.ReadEntryConfirmation() is { Identified: true } read ? read : null;
            return shown is not null;
        }, ConfirmationWaitMs, "run-entry-confirmation", ObservePollFrames);
        if (rejection is { } text)
        {
            Diag($"Entry run {session.RunId}: the game refused the entry ({text}) instead of showing the confirmation.");
            return null;
        }

        if (shown is null && HousingDialogs.IdentifyEntryConfirmation() is { } other)
        {
            Warn($"Entry run {session.RunId}: {other.AddonName} is open but does not read as the entry confirmation ({(other.Text is { } known ? known.ToString() : "no known row")}); it is left alone.");
            confirmationOpenedByRun = false;
        }

        return shown;
    }

    private async Task<bool> CloseConfirmation()
    {
        var deadline = Environment.TickCount64 + ConfirmationCloseWaitMs;
        while (ConfirmationOpen() && Environment.TickCount64 < deadline)
        {
            CloseConfirmationNow();
            await NextFrame(ObservePollFrames);
        }

        var closed = !ConfirmationOpen();
        if (closed)
        {
            confirmationOpenedByRun = false;
        }

        Diag($"Entry run {session.RunId}: the entry confirmation {(closed ? "is closed" : "did not close")}.");
        return closed;
    }

    private static bool ConfirmationOpen() => HousingDialogs.IdentifyEntryConfirmation() is { Text: HousingText.EntryConfirmation };

    private async Task<EntryFailure> Submit(long deposit)
    {
        if (!await TickCheckbox())
        {
            await CloseConfirmation();
            return EntryFailure.CheckboxNotTicked;
        }

        if (HousingDialogs.ReadEntryConfirmation() is not { Identified: true, ConfirmEnabled: true } again)
        {
            await CloseConfirmation();
            return EntryFailure.ConfirmationNotReady;
        }

        if (again.Deposit != deposit)
        {
            await CloseConfirmation();
            return EntryFailure.ChangedPrice;
        }

        if (CharacterContextReader.ReadGil() is not { } gilBefore)
        {
            await CloseConfirmation();
            return EntryFailure.GilUnknown;
        }

        // Confirm is pressed once and never again, so the press window of the tick must be over before the intent.
        if (!await WaitUntilTimed(HousingDialogs.ClickReady, ClickWindowWaitMs, "run-confirm-window", ObservePollFrames))
        {
            await CloseConfirmation();
            return EntryFailure.ConfirmationNotReady;
        }

        var cycle = session.LiveCycle ?? session.Policy.Cycle.Cycle;
        var intent = new ActionAttempt
        {
            AttemptId = Guid.NewGuid(),
            Kind = ActionKind.Entry,
            RunId = session.RunId,
            CharacterContentId = session.Settings.ContentId,
            HomeWorldId = Svc.PlayerState.HomeWorld.RowId,
            Cycle = cycle,
            Plot = session.Target,
            Buyer = session.Settings.Buyer,
            Amount = deposit,
            PreparedUtc = DateTime.UtcNow,
            GilBefore = gilBefore,
        };
        session.Attempt = intent;
        Move(EntryStep.PersistIntent);
        var journal = services.Housing.Journal!;
        var outcome = await new EntrySubmission(journal).Submit(intent, ReturnToGame, () => PressConfirm(deposit), static () => DateTime.UtcNow);
        RunLog.Info($"Entry run {session.RunId}: submission of attempt {intent.AttemptId} for {intent.Plot}, {deposit} gil, cycle {cycle}: {outcome}.");
        switch (outcome)
        {
            case SubmissionOutcome.Dispatched:
            case SubmissionOutcome.DispatchUnrecorded:
                await AwaitReceipt(journal, intent, gilBefore, outcome == SubmissionOutcome.DispatchUnrecorded);
                return EntryFailure.None;
            case SubmissionOutcome.PressRefused:
                await CloseConfirmation();
                session.Fail(EntryFailure.ConfirmRefused);
                Move(EntryStep.NeedsPlayer);
                return EntryFailure.None;
            case SubmissionOutcome.SlotTaken:
                await CloseConfirmation();
                Stop(EntryFailure.AlreadyEntered);
                return EntryFailure.None;
            default:
                await CloseConfirmation();
                Stop(EntryFailure.JournalUnavailable);
                return EntryFailure.None;
        }
    }

    private async Task ReturnToGame() => await NextFrame();

    // The single press. The character and the dialog are read once more on this frame: the same character, and still
    // row 7046 showing the same amount.
    private DialogActionResult PressConfirm(long deposit)
    {
        Move(EntryStep.SubmitOnce);
        if (Svc.PlayerState.ContentId != session.Settings.ContentId
            || HousingDialogs.ReadEntryConfirmation() is not { Identified: true, ConfirmEnabled: true } shown
            || shown.Deposit != deposit)
        {
            return DialogActionResult.IdentityMismatch;
        }

        dispatchSequence = GameSequence.Next();
        dispatchedUtc = DateTime.UtcNow;
        using (NativeActionGate.Allow(NativeAction.ConfirmEntry))
        {
            return HousingDialogs.ConfirmEntry();
        }
    }

    // The tick follows the Enter Lottery press closely, so a throttled tick waits out the press window and tries again,
    // a bounded number of times.
    private async Task<bool> TickCheckbox()
    {
        var ticked = DialogActionResult.Throttled;
        for (var attempt = 1; attempt <= TickAttempts && ticked == DialogActionResult.Throttled; attempt++)
        {
            await WaitUntilTimed(HousingDialogs.ClickReady, ClickWindowWaitMs, "run-tick-window", ObservePollFrames);
            using (NativeActionGate.Allow(NativeAction.TickEntryCheckbox))
            {
                ticked = HousingDialogs.TickEntryCheckbox();
            }

            Diag($"Entry run {session.RunId}: checkbox 'I understand and wish to proceed.' {ticked} (attempt {attempt}/{TickAttempts}).");
        }

        if (ticked is not (DialogActionResult.Pressed or DialogActionResult.AlreadyChecked))
        {
            return false;
        }

        return await WaitUntilTimed(
            () => HousingDialogs.ReadEntryConfirmation() is { Proceed: CheckBoxState.Checked, ConfirmEnabled: true },
            CheckboxWaitMs,
            "run-entry-checkbox",
            ObservePollFrames);
    }

    // After Confirm: the gil and the game's refusals are watched, the windows are closed, and a fresh lottery reading
    // decides. Nothing here presses anything again.
    private async Task AwaitReceipt(ActionJournal journal, ActionAttempt intent, long gilBefore, bool dispatchUnrecorded)
    {
        Move(EntryStep.AwaitReceipt);
        services.Observers.HousingLog.BeginCapture();
        long? gilAfter = null;
        await WaitUntilTimed(() =>
        {
            gilAfter = CharacterContextReader.ReadGil();
            if (services.Observers.HousingLog.FirstRejectionAfter(dispatchSequence) is { } refused)
            {
                rejection = refused.Text;
                return true;
            }

            return gilAfter is { } gil && gilBefore - gil == intent.Amount;
        }, ReceiptGilWaitMs, "run-entry-gil", ObservePollFrames);

        if (ConfirmationOpen())
        {
            Warn($"Entry run {session.RunId}: the entry confirmation is still open after Confirm; it is closed, never pressed again.");
            CloseConfirmationNow();
        }

        HousingDialogs.ClosePlacard();
        LotterySnapshot? refreshed = null;
        if (CharacterContextReader.Read() is { ContentId: var contentId } context && contentId == intent.CharacterContentId)
        {
            refreshed = await RefreshLottery(context);
        }

        gilAfter = CharacterContextReader.ReadGil();
        var facts = new ReceiptFacts(intent.Amount, gilBefore, gilAfter, rejection, refreshed, NamesPlot(refreshed, intent));
        if (ReceiptCorrelation.NeedsSecondReading(facts))
        {
            Diag($"Entry run {session.RunId}: the first reading shows no entry on {intent.Plot} and the gil is untouched; reading again in {SecondReadingDelayMs / TimeUnits.MillisecondsPerSecond}s before the slot is released.");
            await DelayMs(SecondReadingDelayMs);
            var again = CharacterContextReader.Read() is { ContentId: var againId } againContext && againId == intent.CharacterContentId ? await RefreshLottery(againContext) : null;
            rejection ??= services.Observers.HousingLog.FirstRejectionAfter(dispatchSequence)?.Text;
            facts = facts with
            {
                GilAfter = CharacterContextReader.ReadGil(),
                Rejection = rejection,
                SecondReading = again,
                SecondReadingNamesPlot = NamesPlot(again, intent),
            };
        }

        var result = ReceiptCorrelation.Correlate(facts);
        services.Observers.HousingLog.EndCapture();
        RunLog.Info($"Entry run {session.RunId}: receipt for attempt {intent.AttemptId}: {result.Verdict} (gil {gilBefore} -> {facts.GilAfter?.ToString() ?? "unknown"}, deposit {intent.Amount}, "
                  + $"refusal {facts.Rejection?.ToString() ?? "none"}, reading {Describe(facts.Reading)} naming the plot {facts.ReadingNamesPlot?.ToString() ?? "unknown"}, "
                  + $"second reading {Describe(facts.SecondReading)} naming the plot {facts.SecondReadingNamesPlot?.ToString() ?? "unknown"}).");
        switch (result.Verdict)
        {
            case ReceiptVerdict.Confirmed when !dispatchUnrecorded:
                await PersistReceipt(journal, intent, result.LotteryNumber);
                return;
            case ReceiptVerdict.Rejected when !dispatchUnrecorded:
                var rejected = await Task.Run(() => journal.Reject(intent.AttemptId, DateTime.UtcNow));
                await NextFrame();
                Diag($"Entry run {session.RunId}: attempt {intent.AttemptId} recorded as rejected ({rejected}); the slot is free again.");
                FinishWith(EntryFailure.Rejected);
                return;
            default:
                var marked = await Task.Run(() => journal.RequireReconciliation(intent.AttemptId, DateTime.UtcNow));
                await NextFrame();
                Diag($"Entry run {session.RunId}: attempt {intent.AttemptId} needs reconciliation ({marked}); it will not be submitted again.");
                session.Fail(EntryFailure.SubmissionUncertain, rejection);
                Move(EntryStep.NeedsReconciliation);
                return;
        }
    }

    private static bool? NamesPlot(LotterySnapshot? reading, ActionAttempt intent)
        => reading?.Entry is { } entry ? ReadingAddress.Names(entry, intent.Plot, DistrictNames.Of) : null;

    private static string Describe(LotterySnapshot? reading) => reading is { } shown ? LotteryReader.Describe(shown) : "none";

    private async Task PersistReceipt(ActionJournal journal, ActionAttempt intent, uint? lotteryNumber)
    {
        Move(EntryStep.PersistReceipt);
        var receipt = new EntryReceipt
        {
            ReceiptId = Guid.NewGuid(),
            CharacterContentId = intent.CharacterContentId,
            HomeWorldId = intent.HomeWorldId,
            Cycle = intent.Cycle,
            Plot = intent.Plot,
            Buyer = intent.Buyer,
            Evidence = ReceiptEvidence.NativeConfirmation,
            EntryNumber = lotteryNumber is { } number && number <= int.MaxValue ? (int)number : null,
            Deposit = intent.Amount,
            EnteredUtc = dispatchedUtc,
        };
        var written = await Task.Run(() => journal.Confirm(intent.AttemptId, receipt, DateTime.UtcNow));
        await NextFrame();
        RunLog.Info($"Entry run {session.RunId}: receipt {receipt.ReceiptId} for {receipt.Plot}, number {receipt.EntryNumber?.ToString() ?? "unknown"}, {receipt.Deposit} gil: {written}.");
        if (written is not (JournalWrite.Written or JournalWrite.AlreadyRecorded))
        {
            session.Fail(EntryFailure.JournalUnavailable);
            Move(EntryStep.NeedsReconciliation);
            return;
        }

        if (session.Ledger.TryConfirm(intent.Amount, out var confirmed))
        {
            session.Ledger = confirmed.TryRelease(confirmed.PendingReservations, out var released) ? released : confirmed;
            session.Reserved = 0;
        }

        Move(EntryStep.FinishCharacter);
        Move(EntryStep.Completed);
    }
}
