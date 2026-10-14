using AutoHouseLottery.Core.External;
using AutoHouseLottery.Core.Game.Character;
using AutoHouseLottery.Core.Game.Watchers;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Integration;
using AutoHouseLottery.Core.Ipc;
using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Settlement;
using AutoHouseLottery.Core.Settlement.Model;
using AutoHouseLottery.Core.Tasks.Model;
using AutoHouseLottery.Core.Travel;
using ECommons.DalamudServices;
using ClibServices = clib.Services.Svc;

namespace AutoHouseLottery.Core.Tasks;

// Settlement visits under the same execution lease as entry runs: one at a time, never while anything else drives the
// game. A visit started by the scheduler is paused the moment the player acts, unless it is past its intent; a visit in
// its press window always finishes verifying first. The real-refund and real-claim permissions live here, in memory.
internal sealed partial class RunController
{
    private readonly SettlementServices settlementServices;
    private readonly SettlementScheduler scheduler;
    private readonly SettlementNoticeAnnouncer notices;
    private SettlementTask? settlement;
    private LeaseOwner? settlementOwner;
    private SettlementWork? scheduledWork;
    private SettlementFailure stopReason;
    private SettlementGrant? refundGrant;
    private SettlementGrant? claimGrant;
    private SettlementStatus lastSettlement = SettlementStatus.Idle;
    private Guid settlementSession = Guid.NewGuid();

    public bool Settling => settlement is not null;

    public SettlementStatus Settlement => settlement?.Progress ?? lastSettlement;

    public SchedulerBlock SchedulerBlock => scheduler.Block;

    public SettlementRefusal StartSettlement(Guid id, SettlementMode mode, SettlementOrigin origin, bool dryRun)
    {
        var context = CharacterContextReader.Read();
        var refusal = Busy || TaskStarter.Busy ? SettlementRefusal.Busy
            : context is null ? SettlementRefusal.NotLoggedIn
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
        var visit = new SettlementTask(new SettlementRequest(id, mode, origin, dryRun, allowance, configuration.Find.GilReserve), settlementServices);
        settlement = visit;
        settlementOwner = leaseGrant.Owner;
        stopReason = SettlementFailure.StoppedByPlayer;
        lastSettlement = SettlementStatus.Idle;
        RunLog.Info($"Settlement visit authorized: {mode} for {id}, asked by {origin}, {(dryRun ? "dry run requested" : "presses allowed where a permission covers them")}.");
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
        pendingJournalWrite = visit.LeaveOpenAttempt() ?? pendingJournalWrite;
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

    // A maintainer's permission for one real refund or one real claim, in memory only: the logged-in character, one plot
    // on its home world and the one unsettled receipt it entered there. It ends at logout, at unload, or with the one press
    // it opens.
    public SettlementGrant? GrantRealSettlement(ActionKind kind, string characterName, uint districtId, int ward, int plot)
    {
        if (kind == ActionKind.Entry
            || CharacterContextReader.Read() is not { } context
            || !string.Equals(context.Name, characterName, StringComparison.OrdinalIgnoreCase)
            || !PlotKey.TryCreate(HousingContext.Region, context.HomeWorldId, districtId, ward, plot, out var key)
            || UnsettledReceiptAt(context.ContentId, key) is not { } receiptId)
        {
            return null;
        }

        var granted = new SettlementGrant(kind, context.ContentId, context.Name, key, receiptId, DateTime.UtcNow);
        if (kind == ActionKind.Refund)
        {
            refundGrant = granted;
        }
        else
        {
            claimGrant = granted;
        }

        RunLog.Warning($"REAL {kind.ToString().ToUpperInvariant()} ALLOWED for this session: {context.Name}, {key}, receipt {receiptId}, one press only. It ends at logout or unload.");
        return granted;
    }

    public void RevokeRealSettlement()
    {
        if (refundGrant is null && claimGrant is null)
        {
            return;
        }

        refundGrant = null;
        claimGrant = null;
        RunLog.Warning("Real refund and claim permissions revoked; every settlement is a dry run again.");
    }

    // Whether a refund already known to be owed can be collected for real now, so the scheduler knows a visit would do
    // more than inspect it again.
    public bool RefundCollectable(ulong contentId, Guid receiptId, PlotKey plot)
        => SettlementGate.MayPress(ActionKind.Refund, SettlementGate.Validated(ActionKind.Refund), dryRun: false, refundGrant, contentId, plot, receiptId);

    public bool AutomaticRefunds(ulong contentId) => Plugin.Instance.Configuration.AutomaticRefunds(contentId);

    private Guid? UnsettledReceiptAt(ulong contentId, PlotKey plot)
    {
        var snapshot = housing.JournalSnapshot;
        var receipts = snapshot.Receipts;
        for (var index = 0; index < receipts.Length; index++)
        {
            var receipt = receipts[index];
            if (receipt.CharacterContentId == contentId && receipt.Plot == plot && SettlementLookups.Settlement(snapshot, receipt.ReceiptId) is { IsSettled: false })
            {
                return receipt.ReceiptId;
            }
        }

        return null;
    }

    // The refund driver for an entry run's SettleOutstanding step, run inside the run's own lease.
    private SettlementTask CollectForEntry(Guid receiptId, bool dryRun)
    {
        var context = CharacterContextReader.Read();
        var allowance = context is null ? 0 : scheduler.TravelAllowance(context.ContentId, context.Gil);
        return new SettlementTask(new SettlementRequest(receiptId, SettlementMode.Refund, SettlementOrigin.EntryRun, dryRun, allowance, Plugin.Instance.Configuration.Find.GilReserve), settlementServices);
    }

    private SettlementGrant? SettlementGrantFor(ActionKind kind) => kind switch
    {
        ActionKind.Refund => refundGrant,
        ActionKind.Claim => claimGrant,
        _ => null,
    };

    private void ConsumeSettlementGrant(ActionKind kind)
    {
        switch (kind)
        {
            case ActionKind.Refund when refundGrant is { Used: false } open:
                refundGrant = open with { Used = true };
                RunLog.Warning($"Real refund permission for {open.CharacterName}, {open.Plot} used; it opens nothing more.");
                break;
            case ActionKind.Claim when claimGrant is { Used: false } open:
                claimGrant = open with { Used = true };
                RunLog.Warning($"Real claim permission for {open.CharacterName}, {open.Plot} used; it opens nothing more.");
                break;
        }
    }

    private void RecordSettlementTravel(ulong contentId, long spent) => scheduler.RecordTravel(contentId, spent);

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
        refundGrant = null;
        claimGrant = null;
        scheduler.Reset();
        if (settlement is not { } visit)
        {
            return;
        }

        RunLog.Info($"Settlement {visit.Progress.VisitId}: ended at {visit.Progress.Step} ({reason}).");
        stopReason = SettlementFailure.Interrupted;
        pendingJournalWrite = visit.LeaveOpenAttempt() ?? pendingJournalWrite;
        ClibServices.Automation.Stop();
        FinishSettlement(visit);
    }

    // Once a second: the scheduler's view of what is due, the pause of an automatic visit when the player comes back,
    // and the start of the next automatic visit when everything allows it.
    private void TickSettlement(long now)
    {
        var ownWork = Busy || TaskStarter.Busy || HousingTravel.CurrentToken is not null;
        var otherWork = TaskStarter.Busy || lease.Owner is not null || LifestreamIpc.Instance.IsBusy() == true;
        var contentId = Svc.PlayerState.ContentId;
        var next = scheduler.Tick(now, ownWork, Busy || otherWork, Settling, (receiptId, plot) => RefundCollectable(contentId, receiptId, plot));
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

        if (StartSettlement(due.Id, due.Mode, SettlementOrigin.Scheduler, dryRun: false) == SettlementRefusal.None)
        {
            scheduler.Started(due, now);
            scheduledWork = due;
        }
    }

    private static LeaseWork LeaseOf(SettlementMode mode) => mode == SettlementMode.Claim ? LeaseWork.Claim : LeaseWork.Refund;
}
