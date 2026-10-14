using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Tasks.Model;

// The published state of the run, rebuilt on every change and read by the windows.
internal sealed record RunStatus
{
    public static readonly RunStatus Idle = new();

    public bool Active { get; init; }

    public Guid RunId { get; init; }

    public RunMode Mode { get; init; }

    public EntryStep Step { get; init; } = EntryStep.Ready;

    public ulong ContentId { get; init; }

    public string CharacterName { get; init; } = string.Empty;

    public PlotKey? Target { get; init; }

    public int Inspection { get; init; }

    public int InspectionLimit { get; init; }

    public long? LiveDeposit { get; init; }

    public int ConfirmedEntries { get; init; }

    public long Deposited { get; init; }

    public long RemainingCap { get; init; }

    public EntryFailure Failure { get; init; }

    public HousingText? Rejection { get; init; }

    public bool PauseRequested { get; init; }

    public bool StopRequested { get; init; }

    public DateTime StartedUtc { get; init; }

    public bool FinishingVerification => EntrySteps.InSubmissionWindow(Step);

    public bool AwaitsPlayer => Active && Step is EntryStep.NeedsPlayer or EntryStep.Paused;
}
