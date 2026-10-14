using System.Threading.Tasks;
using AutoHouseLottery.Core.External;
using AutoHouseLottery.Core.Game;
using AutoHouseLottery.Core.Game.Character;
using AutoHouseLottery.Core.Game.Watchers;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Ipc;
using AutoHouseLottery.Core.Settlement;
using AutoHouseLottery.Core.Settlement.Model;
using AutoHouseLottery.Core.Tasks.Model;
using AutoHouseLottery.Core.Travel;
using ECommons.DalamudServices;
using ClibServices = clib.Services.Svc;

namespace AutoHouseLottery.Core.Tasks;

// Settlement visits under the same execution lease as entry runs: one at a time, never while anything else drives the
// game. A visit started by the scheduler is paused the moment the player acts, unless it is past its intent; a visit in
// its press window always finishes verifying first.
internal sealed partial class RunController
{
    private readonly SettlementServices settlementServices;
    private readonly SettlementScheduler scheduler;
    private readonly SettlementNoticeAnnouncer notices;
    private SettlementTask? settlement;
    private LeaseOwner? settlementOwner;
    private SettlementWork? scheduledWork;
    private SettlementFailure stopReason;
    private SettlementStatus lastSettlement = SettlementStatus.Idle;
    private Guid settlementSession = Guid.NewGuid();

    public bool Settling => settlement is not null;

    public SettlementStatus Settlement => settlement?.Progress ?? lastSettlement;

    public SchedulerBlock SchedulerBlock => scheduler.Block;

    public SettlementRefusal StartSettlement(Guid id, SettlementMode mode, SettlementOrigin origin)
    {
        var context = CharacterContextReader.Read();
        var region = context is null ? WorldRegion.Unknown : WorldRegionReader.Of(context.HomeWorldId);
        var refusal = Busy || TaskStarter.Busy ? SettlementRefusal.Busy
            : context is null ? SettlementRefusal.NotLoggedIn
            : region == WorldRegion.Unknown ? SettlementRefusal.UnknownRegion
            : !WorldRegions.Supported(region) ? SettlementRefusal.UnsupportedRegion
            : DutyWatcher.InContent() ? SettlementRefusal.InContent
            : !housing.JournalWritable ? SettlementRefusal.JournalUnavailable
            : AutoAnswerGuard.Blocks ? SettlementRefusal.AutoAnswerPlugin
            : SettlementRefusal.None;
        if (refusal != SettlementRefusal.None || !lease.TryAcquire(LeaseOf(mode), out var leaseGrant))
        {
            RunLog.Info($"Settlement not started ({mode} for {id}, asked by {origin}): {(refusal == SettlementRefusal.None ? SettlementRefusal.Busy : refusal)}.");
            return refusal == SettlementRefusal.None ? SettlementRefusal.Busy : refusal;
        }

        var configuration = Plugin.Instance.Configuration;
        long? allowance = origin == SettlementOrigin.Scheduler ? scheduler.TravelAllowance(context!.ContentId, context.Gil) : null;
        var visit = new SettlementTask(new SettlementRequest(id, mode, origin, allowance, configuration.Find.GilReserve), settlementServices);
        settlement = visit;
        settlementOwner = leaseGrant.Owner;
        stopReason = SettlementFailure.StoppedByPlayer;
        lastSettlement = SettlementStatus.Idle;
        RunLog.Info($"Settlement visit authorized: {mode} for {id}, asked by {origin}.");
        try
        {
            ClibServices.Automation.Start(visit, OnCompleted: () => _ = Svc.Framework.RunOnFrameworkThread(() => FinishSettlement(visit)));
        }
        catch (Exception exception)
        {
            RunLog.Error(exception, "The settlement visit could not be started");
            FinishSettlement(visit);
            return SettlementRefusal.Busy;
        }

        return SettlementRefusal.None;
    }

    // A GM alert stops a visit at once wherever it is; a press it may have made is left for reconciliation rather than
    // verified while the alert is on screen.
    public void StopSettlementNow()
    {
        if (settlement is not { } visit)
        {
            return;
        }

        RunLog.Info($"Settlement {visit.Progress.VisitId}: stopped at once at {visit.Progress.Step} for an alert.");
        stopReason = SettlementFailure.Interrupted;
        TrackJournalWrite(visit.LeaveOpenAttempt());
        ClibServices.Automation.Stop();
        FinishSettlement(visit);
    }

    // Stops a visit before its intent. One that may have pressed Yes finishes verifying instead.
    public bool StopSettlement(SettlementFailure reason = SettlementFailure.StoppedByPlayer)
    {
        if (settlement is not { } visit)
        {
            return false;
        }

        if (!SettlementSteps.MayInterrupt(visit.Progress.Step))
        {
            RunLog.Info($"Settlement {visit.Progress.VisitId}: stop asked at {visit.Progress.Step}; the visit ends once the press it made is verified.");
            return false;
        }

        stopReason = reason;
        ClibServices.Automation.Stop();
        FinishSettlement(visit);
        return true;
    }

    public void DismissSettlement()
    {
        if (settlement is null)
        {
            lastSettlement = SettlementStatus.Idle;
        }
    }

    public bool AutomaticRefunds(ulong contentId) => Plugin.Instance.Configuration.AutomaticRefunds(contentId);

    // The refund driver for an entry run's SettleOutstanding step, run inside the run's own lease.
    private SettlementTask CollectForEntry(Guid receiptId)
    {
        var context = CharacterContextReader.Read();
        var allowance = context is null ? 0 : scheduler.TravelAllowance(context.ContentId, context.Gil);
        return new SettlementTask(new SettlementRequest(receiptId, SettlementMode.Refund, SettlementOrigin.EntryRun, allowance, Plugin.Instance.Configuration.Find.GilReserve), settlementServices);
    }

    private void RecordSettlementTravel(ulong contentId, long spent) => scheduler.RecordTravel(contentId, spent);

    // The player's word that an entry was settled in game by hand. Refused while a run or a visit drives the game, so
    // nothing reads the same entry while it is written.
    public bool MarkSettledInGame(Guid receiptId)
    {
        if (Busy || !housing.JournalWritable)
        {
            return false;
        }

        TrackJournalWrite(housing.MarkSettledInGame(receiptId));
        return true;
    }

    // A journal write started outside the run's own steps, kept with any earlier one still running so an unload waits
    // for both before the journal closes.
    private void TrackJournalWrite(Task? write)
    {
        if (write is null)
        {
            return;
        }

        pendingJournalWrite = pendingJournalWrite is { IsCompleted: false } earlier ? Task.WhenAll(earlier, write) : write;
    }

    private Guid SettlementSession() => settlementSession;

    // A new login is a new session for the two-session rule of a placard without a result.
    private void RenewSettlementSession() => settlementSession = Guid.NewGuid();

    private void FinishSettlement(SettlementTask visit)
    {
        if (!ReferenceEquals(settlement, visit))
        {
            return;
        }

        settlement = null;
        scheduledWork = null;
        TrackJournalWrite(visit.PendingWrite);
        if (settlementOwner is { } holder)
        {
            lease.Release(holder);
            settlementOwner = null;
        }

        var progress = visit.Progress;
        lastSettlement = progress.Active || progress.Result == SettlementResult.Stopped
            ? progress with { Active = false, Step = SettlementStep.Finished, Result = SettlementResult.Stopped, Failure = stopReason }
            : progress;
        RunLog.Info($"Settlement {lastSettlement.VisitId}: released ({lastSettlement.Result}{(lastSettlement.Failure == SettlementFailure.None ? string.Empty : $", {lastSettlement.Failure}")}).");
    }

    // Logout or unload ends a visit wherever it is; the visit itself leaves a press it may have made for reconciliation.
    private void EndSettlement(string reason)
    {
        scheduler.Reset();
        if (settlement is not { } visit)
        {
            return;
        }

        RunLog.Info($"Settlement {visit.Progress.VisitId}: ended at {visit.Progress.Step} ({reason}).");
        stopReason = SettlementFailure.Interrupted;
        TrackJournalWrite(visit.LeaveOpenAttempt());
        ClibServices.Automation.Stop();
        FinishSettlement(visit);
    }

    // Once a second: the scheduler's view of what is due, the pause of an automatic visit when the player comes back,
    // and the start of the next automatic visit when everything allows it.
    private void TickSettlement(long now)
    {
        var ownWork = Busy || TaskStarter.Busy || HousingTravel.CurrentToken is not null;
        var otherWork = TaskStarter.Busy || lease.Owner is not null || LifestreamIpc.Instance.IsBusy() == true;
        var next = scheduler.Tick(now, ownWork, Busy || otherWork, Settling);
        if (settlement is { } visit)
        {
            if (visit.Request.Origin == SettlementOrigin.Scheduler
                && scheduledWork is { } work
                && scheduler.IdleMs(now) < now - visit.StartedMs
                && SettlementSteps.MayInterrupt(visit.Progress.Step))
            {
                RunLog.Info($"Settlement {visit.Progress.VisitId}: the player is back; the automatic visit stops before {visit.Progress.Step} goes further and waits for the next idle stretch.");
                scheduler.GiveBack(work);
                StopSettlement(SettlementFailure.PausedForInput);
            }

            return;
        }

        if (Busy || otherWork)
        {
            return;
        }

        if (next is not { } due)
        {
            TryStartRefundSwitch(DateTime.UtcNow);
            return;
        }

        if (StartSettlement(due.Id, due.Mode, SettlementOrigin.Scheduler) == SettlementRefusal.None)
        {
            scheduler.Started(due, now);
            scheduledWork = due;
        }
    }

    private static LeaseWork LeaseOf(SettlementMode mode) => mode == SettlementMode.Claim ? LeaseWork.Claim : LeaseWork.Refund;
}
