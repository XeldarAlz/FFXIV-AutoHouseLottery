using System.Threading.Tasks;
using AutoHouseLottery.Core.External;
using AutoHouseLottery.Core.Game;
using AutoHouseLottery.Core.Game.Character;
using AutoHouseLottery.Core.Game.Dialogs;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Integration;
using AutoHouseLottery.Core.Integration.Model;
using AutoHouseLottery.Core.Persistence;
using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Settlement.Model;
using AutoHouseLottery.Core.Tasks.Model;
using AutoHouseLottery.Core.Travel.Model;
using ECommons.DalamudServices;

namespace AutoHouseLottery.Core.Tasks;

// One visit's orders. Id is a receipt, or an earlier cycle's entry attempt still in question. DryRun: never press Yes,
// whatever is permitted. TravelAllowance: what this visit may spend travelling, null for no session cap (the player's
// own click), with the reserve kept either way.
internal sealed record SettlementRequest(Guid Id, SettlementMode Mode, SettlementOrigin Origin, bool DryRun, long? TravelAllowance, long GilReserve);

// SessionId names this login session, so a placard without a result seen in two different sessions can be told apart
// from one seen twice in a row.
internal sealed record SettlementServices(
    HousingContext Housing,
    HousingObservers Observers,
    Configuration Configuration,
    Func<ActionKind, SettlementGrant?> Grant,
    Action<ActionKind> ConsumeGrant,
    Action<ulong, long> TravelSpent,
    Func<Guid> SessionId);

// The settlement driver of TECHNICAL_DESIGN sections 12 and 13: it travels to an entered plot, reads its result through
// View Lottery Results, records what the result row says, and then either closes the dialog or, for a refund or a claim
// the gate permits, presses Yes once on that row. The intent is on disk before the press, and after it nothing is
// pressed again: the outcome is confirmed from the gil and the same placard read again, or the estate list for a claim,
// or left for reconciliation. Before the intent the controller may stop it; from the intent to the recorded outcome only
// a logout, an unload or a GM alert ends it, and then a press that may have gone through is left for reconciliation.
internal sealed partial class SettlementTask(SettlementRequest request, SettlementServices services) : AutoCommon
{
    private const int ObservePollFrames = 15;
    private const int TimersPassWatchdogMs = 75_000;

    private readonly Guid visitId = Guid.NewGuid();
    private EntryReceipt? receipt;
    private ActionAttempt? uncertainEntry;
    private PlotKey plot;
    private ActionAttempt? ownAttempt;
    private bool pressed;
    private bool attemptSettled;
    private bool resultOpenedByVisit;
    private EntrySubmission? submission;

    public SettlementStatus Progress { get; private set; } = SettlementStatus.Idle;

    public SettlementRequest Request => request;

    public long StartedMs { get; } = Environment.TickCount64;

    // The journal write a visit ended mid-press leaves behind, so an unload can wait for it before the journal closes.
    public Task? PendingWrite { get; private set; }

    private string Label => $"Settlement {visitId}";

    protected override async Task Execute()
    {
        Status = "Settling a lottery entry";
        Progress = new SettlementStatus
        {
            Active = true,
            VisitId = visitId,
            Mode = request.Mode,
            Origin = request.Origin,
            Step = SettlementStep.Ready,
            ReceiptId = request.Id,
        };
        try
        {
            await RunSteps();
        }
        catch (Exception exception) when (CancelToken.IsCancellationRequested)
        {
            if (exception is not OperationCanceledException)
            {
                RunLog.Warning(exception, $"{Label}: an error after the visit was stopped at {Progress.Step}");
            }

            End(SettlementResult.Stopped, Progress.Failure == SettlementFailure.None ? SettlementFailure.Interrupted : Progress.Failure);
        }
        catch (Exception exception)
        {
            RunLog.Error(exception, $"{Label}: faulted at {Progress.Step}");
            End(pressed ? SettlementResult.NeedsReconciliation : SettlementResult.NeedsPlayer, SettlementFailure.Interrupted);
        }
        finally
        {
            ReleaseOwnedWork();
            SettleOpenAttempt();
        }
    }

    private async Task RunSteps()
    {
        Move(SettlementStep.Preflight);
        if (Preflight() is { } refused)
        {
            End(SettlementResult.NeedsPlayer, refused);
            return;
        }

        Move(SettlementStep.ResolvePlot);
        if (ResolvePlot() is not { } resolved)
        {
            End(SettlementResult.NeedsPlayer, SettlementFailure.PlotUnknown);
            return;
        }

        plot = resolved;
        Publish(Progress with { Plot = plot, Real = request.Mode != SettlementMode.Inspect && MayPress(SettlementGate.ActionOf(request.Mode)) });
        if (request.Mode == SettlementMode.Claim && receipt is { } claimed && SettlementLookups.OpenAttempt(services.Housing.JournalSnapshot, claimed.ReceiptId) is { Kind: ActionKind.Claim })
        {
            await ReadBackClaim(claimed);
            return;
        }

        var failure = await Visit();
        if (failure != SettlementFailure.None)
        {
            End(SettlementResult.NeedsPlayer, failure);
            return;
        }

        await InspectAndAct();
    }

    // The logged-in character must be the entrant: the plugin never switches characters, and a cached name never stands
    // in for the character in game. Nothing that opens a result dialog starts while a plugin could answer it.
    private SettlementFailure? Preflight()
    {
        if (CharacterContextReader.Read() is not { } context)
        {
            return SettlementFailure.CharacterChanged;
        }

        if (!services.Housing.JournalWritable)
        {
            return SettlementFailure.JournalUnavailable;
        }

        if (AutoAnswerGuard.Blocks)
        {
            return SettlementFailure.AutoAnswerPlugin;
        }

        var snapshot = services.Housing.JournalSnapshot;
        var current = services.Housing.CurrentPosition(DateTime.UtcNow);
        receipt = SettlementLookups.Receipt(snapshot, request.Id);
        uncertainEntry = receipt is null ? FindUncertainEntry(snapshot, request.Id, current.Cycle) : null;
        var contentId = receipt?.CharacterContentId ?? uncertainEntry?.CharacterContentId;
        if (contentId is null || (receipt is { } held && SettlementLookups.Settlement(snapshot, held.ReceiptId) is not { IsSettled: false }))
        {
            return SettlementFailure.NothingToSettle;
        }

        Publish(Progress with { ContentId = contentId.Value, CharacterName = context.Name, AddressText = receipt?.AddressText });
        if (contentId != context.ContentId)
        {
            return SettlementFailure.CharacterChanged;
        }

        if (receipt is { } entered && !SettlementDeadlines.ResultsArrived(entered.Cycle, current))
        {
            return SettlementFailure.ResultsNotOut;
        }

        if (context.Gil is not { } gil)
        {
            return SettlementFailure.GilUnknown;
        }

        Diag($"{Label}: {request.Mode} for {(receipt is { } found ? $"receipt {found.ReceiptId} (cycle {found.Cycle})" : $"the uncertain entry attempt {request.Id}")}, "
           + $"asked by {request.Origin}{(request.DryRun ? ", dry run requested" : string.Empty)}; gil {gil}, reserve {request.GilReserve}, "
           + $"travel allowance {request.TravelAllowance?.ToString() ?? "no session cap"}.");
        return gil <= request.GilReserve ? SettlementFailure.BelowReserve : null;
    }

    private PlotKey? ResolvePlot()
    {
        if (uncertainEntry is { } attempt)
        {
            return attempt.Plot;
        }

        var entry = receipt!;
        if (entry.Plot is { } known)
        {
            return known;
        }

        if (entry.AddressText is not { } text)
        {
            return null;
        }

        var resolved = ImportedAddress.Resolve(text, entry.Cycle.Region, entry.HomeWorldId, DistrictNames.Of);
        Diag($"{Label}: the Timers address '{text}' {(resolved is { } shown ? $"names {shown} unambiguously" : "names no single plot")}.");
        return resolved;
    }

    private async Task<SettlementFailure> Visit()
    {
        var destination = HousingDestination.Of(plot);
        var arrived = ArrivalCheck.Evaluate(destination, Travel.HousingTravel.ObserveArrival()) == ArrivalVerdict.Arrived;
        Move(SettlementStep.Travel);
        if (!arrived && request.TravelAllowance is { } allowance && allowance < SchedulerRules.TravelLegEstimate)
        {
            return SettlementFailure.TravelBudgetSpent;
        }

        var gilBeforeTravel = CharacterContextReader.ReadGil();
        var travelled = await TravelToPlot(destination, Progress.ContentId, manualArrival: false, Label);
        await RecordTravel(gilBeforeTravel);
        if (travelled != VisitFailure.None)
        {
            return FromVisit(travelled);
        }

        Move(SettlementStep.VerifyArrival);
        var verified = VerifyArrival(destination, Label);
        if (verified != VisitFailure.None)
        {
            return FromVisit(verified);
        }

        Move(SettlementStep.OpenPlacard);
        return FromVisit(await OpenPlacardAt(destination, services.Observers, manualArrival: false, Label));
    }

    // What the trip cost, measured from the gil on either side of it; it is kept on the settlement apart from the refund.
    private async Task RecordTravel(long? gilBefore)
    {
        if (gilBefore is not { } before || CharacterContextReader.ReadGil() is not { } after || after >= before)
        {
            return;
        }

        var spent = before - after;
        Publish(Progress with { TravelSpent = Progress.TravelSpent + spent });
        services.TravelSpent(Progress.ContentId, spent);
        if (receipt is { } entry && services.Housing.Journal is { } journal)
        {
            await Write(() => journal.AddTravel(entry.ReceiptId, spent, DateTime.UtcNow), $"travel of {spent} gil");
        }
    }

    // Only a grant for this very receipt, character and plot opens a real press while the action is not validated.
    private bool MayPress(ActionKind kind)
        => Svc.PlayerState.ContentId == Progress.ContentId
        && SettlementGate.MayPress(kind, SettlementGate.Validated(kind), request.DryRun, services.Grant(kind), Progress.ContentId, plot, receipt?.ReceiptId ?? request.Id);

    private static ActionAttempt? FindUncertainEntry(in JournalSnapshot snapshot, Guid attemptId, CycleKey current)
    {
        var attempts = snapshot.Attempts;
        for (var index = 0; index < attempts.Length; index++)
        {
            if (attempts[index].AttemptId == attemptId && ResultChecks.IsUncertainFromEarlierCycle(attempts[index], current))
            {
                return attempts[index];
            }
        }

        return null;
    }

    private static SettlementFailure FromVisit(VisitFailure failure) => failure switch
    {
        VisitFailure.None => SettlementFailure.None,
        VisitFailure.CharacterChanged => SettlementFailure.CharacterChanged,
        VisitFailure.TravelUnavailable => SettlementFailure.TravelUnavailable,
        VisitFailure.DependencyBusy => SettlementFailure.DependencyBusy,
        VisitFailure.WrongDestination => SettlementFailure.WrongDestination,
        VisitFailure.PlacardNotFound => SettlementFailure.PlacardNotFound,
        VisitFailure.PlacardNotOpened => SettlementFailure.PlacardNotOpened,
        _ => SettlementFailure.TravelFailed,
    };

    private async Task<JournalWrite> Write(Func<JournalWrite> write, string what)
    {
        var written = await Task.Run(write);
        await NextFrame();
        Diag($"{Label}: journal {what}: {written}.");
        return written;
    }

    // Walks the receipt's settlement to the target through the states the journal requires, the amount on the last step.
    private async Task<bool> Advance(ActionJournal journal, Guid receiptId, SettlementState target, long? amount, bool byAbsence = false)
    {
        if (SettlementLookups.Settlement(journal.Snapshot(), receiptId) is not { } current)
        {
            return false;
        }

        var steps = new SettlementState[SettlementPath.MaximumSteps];
        var count = SettlementPath.Plan(current.State, target, steps);
        if (count < 0)
        {
            Warn($"{Label}: receipt {receiptId} cannot go from {current.State} to {target}; nothing is recorded.");
            return false;
        }

        for (var stepIndex = 0; stepIndex < count; stepIndex++)
        {
            var step = steps[stepIndex];
            var last = stepIndex == count - 1;
            var stepAmount = last ? amount : null;
            var written = await Write(() => journal.AdvanceSettlement(receiptId, step, stepAmount, DateTime.UtcNow, last && byAbsence),
                $"settlement of {receiptId} to {step}{(stepAmount is { } shown ? $" ({shown} gil)" : string.Empty)}{(last && byAbsence ? " by absence" : string.Empty)}");
            if (written is not (JournalWrite.Written or JournalWrite.AlreadyRecorded or JournalWrite.WrittenAmountDiffers))
            {
                return false;
            }
        }

        return true;
    }

    // Confirms an attempt by the state the journal holds now, not the copy this visit started from: one still Prepared is
    // first set aside as in question; Dispatched and NeedsReconciliation confirm directly.
    private static JournalWrite ConfirmAttempt(ActionJournal journal, Guid attemptId, EntryReceipt? entered)
    {
        if (SettlementLookups.Attempt(journal.Snapshot(), attemptId) is { } current && SettlementDecisions.NeedsQuestionBeforeConfirm(current.State))
        {
            journal.RequireReconciliation(attemptId, DateTime.UtcNow);
        }

        return journal.Confirm(attemptId, entered, DateTime.UtcNow);
    }

    private void Move(SettlementStep step)
    {
        CancelToken.ThrowIfCancellationRequested();
        Diag($"{Label}: {Progress.Step} -> {step}.");
        Publish(Progress with { Step = step });
    }

    private void Publish(SettlementStatus status) => Progress = status;

    private void End(SettlementResult result, SettlementFailure failure = SettlementFailure.None)
    {
        Publish(Progress with { Active = false, Step = SettlementStep.Finished, Result = result, Failure = failure });
        RunLog.Info($"{Label} ended {result}{(failure == SettlementFailure.None ? string.Empty : $" ({failure})")}"
                  + $"{(Progress.Outcome is { } outcome ? $", outcome {outcome}" : string.Empty)}{(Progress.Received is { } received ? $", {received} gil returned" : string.Empty)}"
                  + $", travel {Progress.TravelSpent} gil.");
    }

    // Owned travel and a result dialog this visit opened and never pressed are given back on the framework thread, which
    // a stopped visit's last continuation may not be on.
    private void ReleaseOwnedWork()
    {
        services.Observers.HousingLog.EndCapture();
        var closeResult = resultOpenedByVisit && !pressed;
        if (Svc.Framework.IsInFrameworkUpdateThread)
        {
            Release(closeResult);
            return;
        }

        _ = Svc.Framework.RunOnFrameworkThread(() => Release(closeResult));
    }

    private void Release(bool closeResult)
    {
        ReleaseVisit();
        if (closeResult)
        {
            CloseResultNow();
        }
    }

    // For the controller ending a visit from outside (logout, unload, a GM alert): the same write the visit's own ending
    // makes, started now so the controller can wait for it before the journal closes. Started once at most.
    public Task? LeaveOpenAttempt()
    {
        SettleOpenAttempt();
        return PendingWrite;
    }

    // A visit ended around its press by a logout, an unload or a GM alert: an intent never pressed is released, and a
    // press that may have gone through is left for reconciliation, never as settled. The write waits for the journal write
    // the submission still has under way, so it never lands before the intent it settles.
    private void SettleOpenAttempt()
    {
        if (ownAttempt is not { } attempt || attemptSettled || PendingWrite is not null || services.Housing.Journal is not { } journal)
        {
            return;
        }

        var receiptId = attempt.ReceiptId.GetValueOrDefault();
        var wasPressed = pressed;
        var inFlight = submission?.InFlight ?? Task.CompletedTask;
        PendingWrite = inFlight.ContinueWith(_ =>
        {
            var written = wasPressed ? journal.RequireReconciliation(attempt.AttemptId, DateTime.UtcNow) : journal.Reject(attempt.AttemptId, DateTime.UtcNow);
            var settlement = wasPressed ? journal.AdvanceSettlement(receiptId, SettlementState.NeedsReconciliation, null, DateTime.UtcNow) : JournalWrite.AlreadyRecorded;
            RunLog.Info($"{Label}: attempt {attempt.AttemptId} {(wasPressed ? "left for reconciliation" : "released, it was never pressed")} ({written}, settlement {settlement}).");
        }, TaskScheduler.Default);
    }

    private static bool CloseResultNow()
    {
        using (NativeActionGate.Allow(NativeAction.CloseResult))
        {
            return HousingDialogs.CloseResult() == DialogActionResult.Closed;
        }
    }
}
