using System.Threading.Tasks;
using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Game;
using AutoHouseLottery.Core.Game.Character;
using AutoHouseLottery.Core.Game.Dialogs;
using AutoHouseLottery.Core.Game.Lottery;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Integration;
using AutoHouseLottery.Core.Integration.Model;
using AutoHouseLottery.Core.Settlement.Model;
using AutoHouseLottery.Core.Tasks.Model;
using AutoHouseLottery.Core.Travel;
using AutoHouseLottery.Core.Travel.Model;

namespace AutoHouseLottery.Core.Tasks;

internal sealed record RunServices(
    HousingContext Housing,
    HousingObservers Observers,
    Func<RealEntryGrant?> Grant,
    Action ConsumeGrant,
    Action CloseConfirmationLater,
    Func<ulong, bool> AutomaticRefunds,
    Func<Guid, bool, SettlementTask> Collect);

// Drives one character's entry through the steps of TECHNICAL_DESIGN section 11. Each await does at most one validated
// operation and every wait has its own deadline. A pause or stop before the intent cancels this task and relaunches
// it later from Preflight; from the intent to the persisted receipt nothing cancels it but a logout or unload.
internal sealed partial class EntryRunTask(EntryRunSession session, RunServices services) : AutoCommon
{
    private const int ObservePollFrames = 15;
    private const int TimersPassWatchdogMs = 75_000;

    // The refund driver's own waits bound the visit; this only catches a visit that never reports back.
    private const int CollectWatchdogMs = 900_000;

    private LotterySnapshot? reading;
    private HousingText? rejection;
    private bool confirmationOpenedByRun;

    protected override async Task Execute()
    {
        Status = "Entering the housing lottery";
        try
        {
            await RunSteps();
        }
        catch (Exception exception) when (CancelToken.IsCancellationRequested)
        {
            if (exception is not OperationCanceledException)
            {
                RunLog.Warning(exception, $"Entry run {session.RunId}: an error after the run was stopped at {session.Step}");
            }
        }
        catch (Exception exception)
        {
            RunLog.Error(exception, $"Entry run {session.RunId}: faulted at {session.Step}");
            session.Fail(EntrySteps.InSubmissionWindow(session.Step) ? EntryFailure.SubmissionUncertain : session.Failure);
            session.ForceEnd("fault");
        }
        finally
        {
            ReleaseOwnedWork();
        }
    }

    private async Task RunSteps()
    {
        if (!await Preflight() || !await SettleOutstanding() || !ReserveFunds())
        {
            return;
        }

        while (true)
        {
            session.BeginInspection();
            var failure = await InspectTarget();
            if (failure == EntryFailure.None || !Decide(failure))
            {
                return;
            }

            session.NextTarget();
        }
    }

    // None when the target's flow ended the run itself; otherwise the failure the fallback rule decides on.
    private async Task<EntryFailure> InspectTarget()
    {
        var failure = await TravelToTarget();
        if (failure != EntryFailure.None)
        {
            return failure;
        }

        failure = await OpenPlacard();
        if (failure != EntryFailure.None)
        {
            return failure;
        }

        var (liveFailure, placard) = await ReadLivePlot();
        if (liveFailure != EntryFailure.None)
        {
            return liveFailure;
        }

        failure = VerifyEligibility(placard!);
        return failure != EntryFailure.None ? failure : await Enter();
    }

    private async Task<bool> Preflight()
    {
        Move(EntryStep.Preflight);
        session.ClearFailure();
        if (CharacterContextReader.Read() is not { } context || context.ContentId != session.Settings.ContentId)
        {
            return Stop(EntryFailure.CharacterChanged);
        }

        if (!services.Housing.JournalWritable)
        {
            return Stop(EntryFailure.JournalUnavailable);
        }

        if (services.Housing.HasPendingAttempt(context.ContentId, session.Policy.Cycle.Cycle))
        {
            session.Fail(EntryFailure.SubmissionUncertain);
            Move(EntryStep.NeedsReconciliation);
            return false;
        }

        if (context.OnHomeWorld)
        {
            reading = await RefreshLottery(context);
        }
        else
        {
            Diag($"Entry run {session.RunId}: not on the home world, so the lottery reading waits; the game itself refuses a second entry.");
        }

        var participation = services.Housing.Participation(context.ContentId, context.HomeWorldId, reading, session.Policy.Cycle);
        Diag($"Entry run {session.RunId}: preflight for {session.CharacterName}, gil {context.Gil?.ToString() ?? "unknown"}, cycle {session.Policy.Cycle.Cycle} "
           + $"({session.Policy.Cycle.Evidence}), entry slot {participation.State}, mode {session.Mode}, {session.EntryMode}, {session.Assignment.TargetOrder.Length} target(s).");
        switch (participation.State)
        {
            case EntrySlotState.Entered:
                return FinishWith(EntryFailure.AlreadyEntered);
            case EntrySlotState.Unresolved:
                session.Fail(EntryFailure.SubmissionUncertain);
                Move(EntryStep.NeedsReconciliation);
                return false;
            default:
                return true;
        }
    }

    // The game refuses a new entry while a refund or a won plot from an earlier entry waits (LogMessage 6100). With
    // automatic refunds on, the refund driver visits that entry first, in this run's own lease and with this run's own
    // dry-run choice; a dry run cannot free the slot, so the run stops there and says so. A win stops for the player.
    private async Task<bool> SettleOutstanding()
    {
        Move(EntryStep.SettleOutstanding);
        var contentId = session.Settings.ContentId;
        var outstanding = services.Housing.Outstanding(contentId, session.Policy.Cycle.Cycle);
        var step = OutstandingPlan.Decide(outstanding.Kind, services.AutomaticRefunds(contentId));
        Diag($"Entry run {session.RunId}: outstanding earlier entry {outstanding.Kind}{(outstanding.ReceiptId is { } held ? $" ({held})" : string.Empty)}; {step}.");
        if (step == OutstandingStep.CollectFirst && outstanding.ReceiptId is { } receiptId)
        {
            step = await CollectOutstanding(receiptId);
            if (session.StopRequested || session.PauseRequested)
            {
                return false;
            }

            if (step == OutstandingStep.Proceed)
            {
                var remaining = services.Housing.Outstanding(contentId, session.Policy.Cycle.Cycle);
                step = remaining.Kind == OutstandingKind.None ? OutstandingStep.Proceed : OutstandingPlan.Decide(remaining.Kind, automaticRefunds: false);
            }
        }

        return step switch
        {
            OutstandingStep.Proceed => true,
            OutstandingStep.StopUnclaimedWin => FinishWith(EntryFailure.UnclaimedWin),
            OutstandingStep.StopDryRunCollection => FinishWith(EntryFailure.RefundDryRun),
            OutstandingStep.StopCheckEarlierResult => FinishWith(EntryFailure.CheckEarlierResult),
            _ => FinishWith(EntryFailure.CollectRefundFirst),
        };
    }

    private async Task<OutstandingStep> CollectOutstanding(Guid receiptId)
    {
        var collect = services.Collect(receiptId, session.DryRunRequested);
        session.Collecting = collect;
        try
        {
            await RunCancellable(collect, CollectWatchdogMs, "run-collect-earlier-refund");
        }
        finally
        {
            session.Collecting = null;
        }

        CancelToken.ThrowIfCancellationRequested();
        var visit = collect.Progress;
        Diag($"Entry run {session.RunId}: the earlier entry's visit ended {visit.Result}{(visit.Failure == Settlement.Model.SettlementFailure.None ? string.Empty : $" ({visit.Failure})")}"
           + $"{(visit.Outcome is { } outcome ? $", outcome {outcome}" : string.Empty)}.");
        return OutstandingPlan.AfterCollection(visit.Result, visit.Outcome);
    }

    private bool ReserveFunds()
    {
        Move(EntryStep.ReserveFunds);
        if (session.Reserved > 0)
        {
            return true;
        }

        var maximum = session.Assignment.MaximumDeposit;
        if (!session.Ledger.TryReserve(maximum, out var reserved))
        {
            return FinishWith(EntryFailure.OverBudget);
        }

        session.Ledger = reserved;
        session.Reserved = maximum;
        Diag($"Entry run {session.RunId}: reserved up to {maximum} gil of the {session.Settings.DepositCap} gil cap, keeping {session.Settings.GilReserve} gil in reserve.");
        return true;
    }

    // A stopped or paused run has already been moved on by the controller, so nothing here may move it again.
    private void Move(EntryStep next)
    {
        CancelToken.ThrowIfCancellationRequested();
        session.MoveTo(next);
    }

    // The fallback rule applied to a failure before payment. True when the run goes on to the next ranked plot.
    private bool Decide(EntryFailure failure)
    {
        CancelToken.ThrowIfCancellationRequested();
        session.Fail(failure, rejection);
        var decision = FallbackRules.Decide(session.Mode, failure, submissionStarted: false, session.InspectionsUsed, session.Policy.MaxInspections, session.HasNextTarget);
        Diag($"Entry run {session.RunId}: {failure} on {session.Target} (inspection {session.InspectionsUsed} of {session.Assignment.TargetOrder.Length}); {decision}.");
        switch (decision)
        {
            case FallbackDecision.TryNextPlot:
                HousingDialogs.ClosePlacard();
                return true;
            case FallbackDecision.FinishCharacter when FallbackRules.IsConclusivePreCommit(failure) && session.Mode == RunMode.BestMatch:
                HousingDialogs.ClosePlacard();
                return FinishWith(EntryFailure.InspectionsExhausted);
            case FallbackDecision.FinishCharacter when RunTransitions.IsLegal(session.Step, EntryStep.FinishCharacter):
                return FinishWith(failure);
            case FallbackDecision.FinishCharacter:
                return Stop(failure);
            default:
                Move(EntryStep.NeedsPlayer);
                return false;
        }
    }

    private bool FinishWith(EntryFailure failure)
    {
        CancelToken.ThrowIfCancellationRequested();
        session.Fail(failure, rejection);
        Move(EntryStep.FinishCharacter);
        Move(EntryStep.CompletedPartial);
        return false;
    }

    private bool Stop(EntryFailure failure)
    {
        CancelToken.ThrowIfCancellationRequested();
        session.Fail(failure, rejection);
        Move(EntryStep.Stopped);
        return false;
    }

    // A fresh Timers pass for the run's character. Any entry it shows that the journal lacks is imported.
    private async Task<LotterySnapshot?> RefreshLottery(CharacterContext context)
    {
        var pass = new LotteryTimersPass();
        await RunCancellable(pass, TimersPassWatchdogMs, "run-lottery-reading");
        if (CancelToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(CancelToken);
        }

        Diag($"Entry run {session.RunId}: lottery reading {pass.Outcome}; {LotteryReader.Describe(pass.Snapshot)}.");
        if (pass.Outcome != LotteryPassOutcome.Loaded || pass.Snapshot.ContentId != context.ContentId)
        {
            return null;
        }

        await services.Housing.ImportFromReading(pass.Snapshot, context.ContentId, context.HomeWorldId);
        await NextFrame();
        return pass.Snapshot;
    }

    private DepositLedger OtherCommitments()
        => session.Reserved > 0 && session.Ledger.TryRelease(session.Reserved, out var others) ? others : session.Ledger;

    // A refusal because an earlier refund or plot still waits (LogMessage 6100) reopens any entry of this character that
    // was recorded as settled only because its placard offered no result.
    private void ReleaseOwnedWork()
    {
        if (rejection == HousingText.OutstandingPropertyOrRefund)
        {
            _ = services.Housing.ReopenAbsenceSettlements(session.Settings.ContentId);
        }

        services.Observers.HousingLog.EndCapture();
        ReleaseVisit();

        if (confirmationOpenedByRun && !EntrySteps.InSubmissionWindow(session.Step) && !CloseConfirmationNow())
        {
            services.CloseConfirmationLater();
        }
    }

    private static bool CloseConfirmationNow()
    {
        using (NativeActionGate.Allow(NativeAction.CancelEntry))
        {
            return HousingDialogs.CancelEntry() == DialogActionResult.Closed;
        }
    }
}
