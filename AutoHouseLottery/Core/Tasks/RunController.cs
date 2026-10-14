using System.Collections.Immutable;
using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Game;
using AutoHouseLottery.Core.Game.Character;
using AutoHouseLottery.Core.Game.Watchers;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Integration;
using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Selection.Model;
using AutoHouseLottery.Core.Tasks.Model;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.DalamudServices;
using ClibServices = clib.Services.Svc;

namespace AutoHouseLottery.Core.Tasks;

// The single owner of game-changing work. It holds the execution lease for the whole of an entry run or a
// reconciliation, so nothing else drives the game meanwhile, and it is the only way a run starts, pauses, resumes or
// stops. Find a home starts runs through StartExact and StartBestMatch with a frozen policy.
internal sealed class RunController
{
    private const int SchedulerPollMs = TimeUnits.MillisecondsPerSecond;

    private readonly ExecutionLease lease = new();
    private readonly HousingContext housing;
    private readonly RunServices services;
    private EntryRunSession? session;
    private EntryRunTask? task;
    private ReconcileTask? reconciling;
    private LeaseOwner? owner;
    private RealEntryGrant? grant;
    private RunStatus finished = RunStatus.Idle;
    private bool reconcileWanted;
    private ulong lastContentId;
    private long nextPollMs;

    public RunController(HousingContext housing, HousingObservers observers)
    {
        this.housing = housing;
        services = new RunServices(housing, observers, () => grant, ConsumeGrant);
        reconcileWanted = true;
        Svc.Framework.Update += OnUpdate;
    }

    public PauseReason PauseReason { get; private set; } = PauseReason.None;

    public bool Paused => PauseReason != PauseReason.None;

    public bool Running => session is not null;

    public bool Reconciling => reconciling is not null;

    public bool Busy => Running || Reconciling;

    public RunStatus Status => session?.Status ?? finished;

    public ReconcileReport LastReconcile { get; private set; }

    public StartRefusal StartExact(RunPolicy policy, bool dryRun = true) => Start(RunMode.Exact, policy, dryRun);

    public StartRefusal StartBestMatch(RunPolicy policy, bool dryRun = true) => Start(RunMode.BestMatch, policy, dryRun);

    public void Pause(PauseReason reason)
    {
        if (reason == PauseReason.None || session is not { } active || EntrySteps.IsTerminal(active.Step))
        {
            return;
        }

        if (Paused)
        {
            if (reason == PauseReason.Manual && PauseReason == PauseReason.InContent)
            {
                PauseReason = PauseReason.Manual;
                RunLog.Info("Auto-pause promoted to a manual pause; leaving content will no longer resume.");
            }

            return;
        }

        if (!RunTransitions.IsLegal(active.Step, EntryStep.Paused))
        {
            if (!active.PauseRequested && active.Step != EntryStep.NeedsPlayer)
            {
                active.PauseRequested = true;
                active.Publish();
                RunLog.Info($"Entry run {active.RunId}: pause asked at {active.Step}; a press may have reached the game, so the run finishes verifying it first.");
            }

            return;
        }

        PauseReason = reason;
        StopTask();
        active.MoveTo(EntryStep.Paused);
        RunLog.Info($"Entry run {active.RunId}: paused ({reason}); Resume starts again from a fresh check of the character and the plot.");
    }

    public void Resume()
    {
        if (session is not { } active)
        {
            PauseReason = PauseReason.None;
            return;
        }

        if (active.Step is not (EntryStep.Paused or EntryStep.NeedsPlayer) || task is not null)
        {
            return;
        }

        PauseReason = PauseReason.None;
        active.PauseRequested = false;
        RunLog.Info($"Entry run {active.RunId}: resuming from {active.Step}.");
        Launch(active);
    }

    public void TogglePause()
    {
        if (Paused || Status.Step == EntryStep.NeedsPlayer)
        {
            Resume();
            return;
        }

        Pause(PauseReason.Manual);
    }

    // The player walks to the plot and opens its placard; the run waits for both instead of travelling.
    public void VisitManually()
    {
        if (session is not { } active)
        {
            return;
        }

        active.ManualArrival = true;
        RunLog.Info($"Entry run {active.RunId}: the player will walk to {active.Target} and open its placard.");
        Resume();
    }

    public void Stop()
    {
        if (session is not { } active)
        {
            return;
        }

        if (EntrySteps.InSubmissionWindow(active.Step) || active.Step == EntryStep.PersistIntent)
        {
            active.StopRequested = true;
            active.Publish();
            RunLog.Info($"Entry run {active.RunId}: stop asked at {active.Step}; the run ends once the press it made is verified.");
            return;
        }

        StopTask();
        active.MoveTo(EntryStep.Stopped);
        Settle(active, EntryStep.Stopped, "stopped by the player");
    }

    public void StopForAlert()
    {
        Stop();
        CancelReconcile();
    }

    public void Interrupt()
    {
        grant = null;
        End("logged out");
    }

    public void Shutdown()
    {
        End("the plugin is unloading");
        Svc.Framework.Update -= OnUpdate;
    }

    public void Dismiss()
    {
        if (session is null)
        {
            finished = RunStatus.Idle;
        }
    }

    public bool Reconcile()
    {
        if (Busy || TaskStarter.Busy || !lease.TryAcquire(LeaseWork.Entry, out var leaseGrant))
        {
            RunLog.Info("Reconciliation not started: the game is busy with other work.");
            return false;
        }

        var reconcile = new ReconcileTask(housing);
        reconciling = reconcile;
        var reconcileOwner = leaseGrant.Owner;
        try
        {
            ClibServices.Automation.Start(reconcile, OnCompleted: () => _ = Svc.Framework.RunOnFrameworkThread(() => FinishReconcile(reconcile, reconcileOwner)));
            return true;
        }
        catch (Exception exception)
        {
            RunLog.Error(exception, "Reconciliation could not be started");
            FinishReconcile(reconcile, reconcileOwner);
            return false;
        }
    }

    // A maintainer's permission for one real entry, kept in memory only. It names the logged-in character, one plot
    // on its home world and a deposit ceiling, and ends at logout, at unload, or after the one entry.
    public RealEntryGrant? GrantRealEntry(string characterName, uint districtId, int ward, int plot, long maxDeposit)
    {
        if (CharacterContextReader.Read() is not { } context
            || !string.Equals(context.Name, characterName, StringComparison.OrdinalIgnoreCase)
            || maxDeposit <= 0
            || !PlotKey.TryCreate(HousingContext.Region, context.HomeWorldId, districtId, ward, plot, out var key))
        {
            return null;
        }

        grant = new RealEntryGrant(context.ContentId, context.Name, key, maxDeposit, DateTime.UtcNow);
        RunLog.Warning($"REAL ENTRY ALLOWED for this session: {context.Name}, {key}, up to {maxDeposit} gil, one entry only. It ends at logout or unload.");
        return grant;
    }

    public void RevokeRealEntry()
    {
        if (grant is null)
        {
            return;
        }

        grant = null;
        RunLog.Warning("Real entry permission revoked; every run is a dry run again.");
    }

    private StartRefusal Start(RunMode mode, RunPolicy policy, bool dryRun)
    {
        var context = CharacterContextReader.Read();
        CharacterRunSettings? settings = context is null ? null : FindSettings(policy, context.ContentId);
        RunPreview? preview = null;
        RunAssignment? assignment = null;
        var slot = EntrySlotState.Unknown;
        if (context is not null && settings is not null && RunPolicyRules.Validate(policy) == RunPolicyError.None)
        {
            var profile = housing.Profile(context, housing.LastReading(context.ContentId), policy.Cycle);
            preview = AssignmentPlanner.Build(policy, [profile], housing.Plots, DateTime.UtcNow);
            assignment = FindAssignment(preview.Assignments, context.ContentId);
            slot = profile.Participation.StateIn(policy.Cycle.Cycle);
        }

        var facts = new StartFacts(
            Busy,
            TaskStarter.Busy || lease.Owner is not null,
            context is not null,
            DutyWatcher.InContent(),
            mode,
            policy,
            settings is not null,
            housing.JournalWritable,
            context is not null && housing.HasPendingAttempt(context.ContentId),
            slot,
            preview?.Blockers ?? RunBlocker.None,
            assignment is not null);
        var refusal = StartRules.Check(facts);
        if (refusal != StartRefusal.None || context is null || settings is not { } chosen || assignment is not { } planned || preview is null)
        {
            RunLog.Info($"Run not started ({mode}): {refusal}{(preview is null ? string.Empty : $", preview blockers {preview.Blockers}")}.");
            return refusal == StartRefusal.None ? StartRefusal.NoAssignment : refusal;
        }

        if (!lease.TryAcquire(LeaseWork.Entry, out var leaseGrant))
        {
            return StartRefusal.Busy;
        }

        owner = leaseGrant.Owner;
        var entryMode = RealEntryGate.ModeFor(EntryCapabilities.EntryConfirmValidated, dryRun, grant, context.ContentId, planned.TargetOrder.AsSpan());
        var active = new EntryRunSession(Guid.NewGuid(), mode, entryMode, dryRun, policy, planned, chosen, context.Name, preview.Ranking);
        session = active;
        finished = RunStatus.Idle;
        PauseReason = PauseReason.None;
        RunLog.Info($"Entry run {active.RunId} authorized ({mode}, {entryMode}): {context.Name}, policy revision {policy.Revision}, cycle {policy.Cycle.Cycle} ({policy.Cycle.Evidence}), "
                  + $"buyer {chosen.Buyer}, cap {chosen.DepositCap} gil, reserve {chosen.GilReserve} gil, targets {Describe(planned.TargetOrder)}.");
        Launch(active);
        return StartRefusal.None;
    }

    private void Launch(EntryRunSession active)
    {
        var runTask = new EntryRunTask(active, services);
        task = runTask;
        try
        {
            ClibServices.Automation.Start(runTask, OnCompleted: () => _ = Svc.Framework.RunOnFrameworkThread(() => HandOff(runTask)));
        }
        catch (Exception exception)
        {
            task = null;
            RunLog.Error(exception, $"Entry run {active.RunId}: the run task could not be started");
            active.ForceEnd("the task could not start");
            Settle(active, active.Step, "the task could not start");
        }
    }

    private void HandOff(EntryRunTask runTask)
    {
        if (!ReferenceEquals(task, runTask))
        {
            return;
        }

        task = null;
        if (session is not { } active)
        {
            return;
        }

        if (EntrySteps.IsTerminal(active.Step))
        {
            Settle(active, active.Step, active.Step.ToString());
            return;
        }

        if (active.StopRequested && RunTransitions.IsLegal(active.Step, EntryStep.Stopped))
        {
            active.MoveTo(EntryStep.Stopped);
            Settle(active, EntryStep.Stopped, "stopped by the player");
            return;
        }

        RunLog.Info($"Entry run {active.RunId}: waiting at {active.Step}{(active.Failure == EntryFailure.None ? string.Empty : $" ({active.Failure})")}.");
    }

    private void End(string reason)
    {
        CancelReconcile();
        if (session is not { } active)
        {
            return;
        }

        var interruptedAt = active.Step;
        StopTask();
        active.ForceEnd(reason);
        Settle(active, interruptedAt, reason);
    }

    // InterruptedAt is the step the run was at when it ended, which can differ from its final step after an interrupt.
    private void Settle(EntryRunSession active, EntryStep interruptedAt, string reason)
    {
        if (!ReferenceEquals(session, active))
        {
            return;
        }

        session = null;
        PauseReason = PauseReason.None;
        if (owner is { } holder)
        {
            lease.Release(holder);
            owner = null;
        }

        SettleAttempt(active, interruptedAt);
        finished = active.Status;
        RunLog.Info($"Entry run {active.RunId} ended {active.Step} ({reason}){(active.Failure == EntryFailure.None ? string.Empty : $", {active.Failure}")}"
                  + $"{(active.Trace is { } trace ? $"; dry run reached the confirmation for {trace.Plot} at {trace.Deposit} gil" : string.Empty)}.");
    }

    // An intent written but never pressed is released; a press that may have reached the game and was never settled
    // is recorded as needing reconciliation.
    private void SettleAttempt(EntryRunSession active, EntryStep interruptedAt)
    {
        if (active.Attempt is not { } attempt || housing.Journal is not { } journal)
        {
            return;
        }

        var unpressed = interruptedAt == EntryStep.PersistIntent;
        if (!unpressed && active.Step != EntryStep.NeedsReconciliation)
        {
            return;
        }

        _ = System.Threading.Tasks.Task.Run(() =>
        {
            var written = unpressed ? journal.Reject(attempt.AttemptId, DateTime.UtcNow) : journal.RequireReconciliation(attempt.AttemptId, DateTime.UtcNow);
            RunLog.Info($"Entry run {active.RunId}: attempt {attempt.AttemptId} {(unpressed ? "released, it was never pressed" : "marked for reconciliation")} ({written}).");
        });
        reconcileWanted = false;
    }

    private void StopTask()
    {
        if (task is null)
        {
            return;
        }

        task = null;
        ClibServices.Automation.Stop();
    }

    private void CancelReconcile()
    {
        if (reconciling is null)
        {
            return;
        }

        ClibServices.Automation.Stop();
    }

    private void FinishReconcile(ReconcileTask reconcile, LeaseOwner reconcileOwner)
    {
        if (!ReferenceEquals(reconciling, reconcile))
        {
            return;
        }

        reconciling = null;
        lease.Release(reconcileOwner);
        LastReconcile = reconcile.Report;
    }

    private void ConsumeGrant()
    {
        if (grant is not { Used: false } open)
        {
            return;
        }

        grant = open with { Used = true };
        RunLog.Warning($"Real entry permission for {open.CharacterName}, {open.Plot} used; it opens nothing more.");
    }

    // Once a second: remember the logged-in character, and read back its uncertain attempts when it is free to.
    private void OnUpdate(IFramework framework)
    {
        var now = Environment.TickCount64;
        if (now < nextPollMs)
        {
            return;
        }

        nextPollMs = now + SchedulerPollMs;
        if (!Svc.ClientState.IsLoggedIn || !Svc.PlayerState.IsLoaded)
        {
            return;
        }

        var contentId = Svc.PlayerState.ContentId;
        if (contentId != lastContentId)
        {
            lastContentId = contentId;
            reconcileWanted = true;
            Plugin.Instance.Configuration.Remember(contentId, Svc.PlayerState.CharacterName, Svc.PlayerState.HomeWorld.RowId);
        }

        if (LastReconcile.Outcome == ReconcileOutcome.NotHomeWorld && Svc.PlayerState.HomeWorld.RowId == Svc.PlayerState.CurrentWorld.RowId)
        {
            LastReconcile = default;
            reconcileWanted = true;
        }

        if (!reconcileWanted || Busy || TaskStarter.Busy || DutyWatcher.InContent() || GenericHelpers.IsOccupied() || Svc.Objects.LocalPlayer is null)
        {
            return;
        }

        reconcileWanted = false;
        if (housing.HasPendingAttempt(contentId))
        {
            RunLog.Info("Reconciliation: this character has an entry attempt in question; reading its lottery entry before any further entry.");
            Reconcile();
        }
    }

    private static CharacterRunSettings? FindSettings(RunPolicy policy, ulong contentId)
    {
        if (policy.Characters.IsDefault)
        {
            return null;
        }

        for (var index = 0; index < policy.Characters.Length; index++)
        {
            if (policy.Characters[index].ContentId == contentId)
            {
                return policy.Characters[index];
            }
        }

        return null;
    }

    private static RunAssignment? FindAssignment(ImmutableArray<RunAssignment> assignments, ulong contentId)
    {
        for (var index = 0; index < assignments.Length; index++)
        {
            if (assignments[index].ContentId == contentId)
            {
                return assignments[index];
            }
        }

        return null;
    }

    private static string Describe(ImmutableArray<PlotKey> targets) => string.Join(", ", targets);
}
