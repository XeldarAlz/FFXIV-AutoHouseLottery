using System.Collections.Immutable;
using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Selection.Model;
using AutoHouseLottery.Core.Settlement.Model;
using AutoHouseLottery.Core.Tasks.Model;

namespace AutoHouseLottery.Core.Tasks;

internal sealed class EntryRunSession
{
    private readonly ImmutableArray<RankedPlot> ranking;

    // CharacterNumber and CharacterCount place this character in a batch; a single run is character 1 of 1.
    public EntryRunSession(
        Guid runId,
        RunMode mode,
        RunPolicy policy,
        RunAssignment assignment,
        CharacterRunSettings settings,
        string characterName,
        ImmutableArray<RankedPlot> ranking,
        int characterNumber = 1,
        int characterCount = 1)
    {
        RunId = runId;
        Mode = mode;
        Policy = policy;
        Assignment = assignment;
        Settings = settings;
        CharacterName = characterName;
        StartedUtc = DateTime.UtcNow;
        CharacterNumber = characterNumber;
        CharacterCount = characterCount;
        this.ranking = ranking;
        Publish();
    }

    public Guid RunId { get; }

    public RunMode Mode { get; }

    public RunPolicy Policy { get; }

    public RunAssignment Assignment { get; }

    public CharacterRunSettings Settings { get; }

    public string CharacterName { get; }

    public DateTime StartedUtc { get; }

    public int CharacterNumber { get; }

    public int CharacterCount { get; }

    public EntryStep Step { get; private set; } = EntryStep.Ready;

    public EntryFailure Failure { get; private set; }

    public HousingText? Rejection { get; private set; }

    public int TargetIndex { get; private set; }

    // Each target is inspected once, in order, so the targets reached so far are the inspections used.
    public int InspectionsUsed => TargetIndex + 1;

    public bool ManualArrival { get; set; }

    public DepositLedger Ledger { get; set; }

    public long Reserved { get; set; }

    public long? LiveDeposit { get; private set; }

    public CycleKey? LiveCycle { get; set; }

    public ActionAttempt? Attempt { get; set; }

    public bool PauseRequested { get; set; }

    // Why the deferred pause was asked for, so a pause for a duty can still resume on its own afterwards.
    public PauseReason RequestedPause { get; set; }

    public bool StopRequested { get; set; }

    // The refund driver's visit to an earlier entry while SettleOutstanding runs it, so a pause or stop waits for a press
    // it may have made.
    public SettlementTask? Collecting { get; set; }

    public bool CollectingInPressWindow => Collecting is { } visit && SettlementSteps.InPressWindow(visit.Progress.Step);

    public RunStatus Status { get; private set; } = RunStatus.Idle;

    public PlotKey Target => Assignment.TargetOrder[TargetIndex];

    public bool HasNextTarget => TargetIndex + 1 < Assignment.TargetOrder.Length;

    public CharacterBudget Budget => new(Settings.DepositCap, Settings.GilReserve, Policy.TravelAllowance);

    // The amount the run was started with for this plot: the exact plot's bound, or the deposit the ranking knew.
    public long? ExpectedDeposit(PlotKey plot)
    {
        if (Policy.Exact is { } exact)
        {
            return exact.Plot == plot ? exact.Deposit : null;
        }

        for (var index = 0; index < ranking.Length; index++)
        {
            if (ranking[index].Key == plot)
            {
                return ranking[index].Deposit;
            }
        }

        return null;
    }

    public void MoveTo(EntryStep next)
    {
        if (!RunTransitions.IsLegal(Step, next))
        {
            throw new InvalidOperationException($"Entry run {RunId}: {Step} cannot move to {next}.");
        }

        RunLog.Info($"Entry run {RunId}: {Step} -> {next}{(next == EntryStep.Travel ? $" (target {TargetIndex + 1} of {Assignment.TargetOrder.Length}, {Target})" : string.Empty)}.");
        Step = next;
        Publish();
    }

    // For an interruption the run cannot wait out: a press that may have reached the game becomes a reconciliation.
    public void ForceEnd(string reason)
    {
        var end = RunTransitions.OnInterrupt(Step);
        RunLog.Info($"Entry run {RunId}: {Step} -> {end} ({reason}).");
        Step = end;
        Publish();
    }

    public void Fail(EntryFailure failure, HousingText? rejection = null)
    {
        Failure = failure;
        Rejection = rejection;
        RunLog.Info($"Entry run {RunId}: {failure}{(rejection is { } text ? $" ({text})" : string.Empty)} at {Step} for {Target}.");
        Publish();
    }

    public void ClearFailure()
    {
        Failure = EntryFailure.None;
        Rejection = null;
        Publish();
    }

    public void BeginInspection()
    {
        LiveDeposit = null;
        Publish();
    }

    public void NextTarget()
    {
        TargetIndex++;
        Publish();
    }

    public void ReadDeposit(long? deposit)
    {
        LiveDeposit = deposit;
        Publish();
    }

    public void Publish()
    {
        Status = new RunStatus
        {
            Active = !EntrySteps.IsTerminal(Step),
            RunId = RunId,
            Mode = Mode,
            Step = Step,
            ContentId = Settings.ContentId,
            CharacterName = CharacterName,
            CharacterNumber = CharacterNumber,
            CharacterCount = CharacterCount,
            Target = Target,
            Inspection = InspectionsUsed,
            InspectionLimit = Assignment.TargetOrder.Length,
            LiveDeposit = LiveDeposit,
            ConfirmedEntries = Ledger.GrossSubmitted > 0 ? 1 : 0,
            Deposited = Ledger.GrossSubmitted,
            RemainingCap = Math.Max(0, Settings.DepositCap - Ledger.GrossSubmitted),
            Failure = Failure,
            Rejection = Rejection,
            PauseRequested = PauseRequested,
            StopRequested = StopRequested,
            StartedUtc = StartedUtc,
        };
    }
}
