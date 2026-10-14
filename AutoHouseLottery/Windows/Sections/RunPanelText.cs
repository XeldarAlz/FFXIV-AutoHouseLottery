using AutoHouseLottery.Core.Integration;
using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Core.Tasks;
using AutoHouseLottery.Core.Tasks.Model;
using Dalamud.Interface;
using System.Numerics;

namespace AutoHouseLottery.Windows.Sections;

// The run panel's and the mini player's words, formatted once per published run status, pause state and language, so
// drawing a frame allocates nothing; only the running time is formatted again, once a second.
internal sealed class RunPanelText
{
    public static readonly RunPanelText Shared = new();

    private const string Separator = " · ";

    private RunStatus? builtFor;
    private bool builtPaused;
    private LanguageInfo? builtLanguage;
    private long builtSecond = -1;

    public string Title { get; private set; } = string.Empty;

    public string Address { get; private set; } = string.Empty;

    public string StepLine { get; private set; } = string.Empty;

    public string Summary { get; private set; } = string.Empty;

    public string Failure { get; private set; } = string.Empty;

    public string? Facts { get; private set; }

    public string Timing { get; private set; } = string.Empty;

    public string Inspection { get; private set; } = string.Empty;

    public string LiveDeposit { get; private set; } = string.Empty;

    public string RunningLabel { get; private set; } = string.Empty;

    public (FontAwesomeIcon Icon, Vector4 Accent, string Title, string Body) Outcome { get; private set; }

    public RunPanelText For(RunController controller)
    {
        var status = controller.Status;
        var paused = controller.Paused;
        if (!ReferenceEquals(status, builtFor) || paused != builtPaused || !ReferenceEquals(Loc.Current, builtLanguage))
        {
            builtFor = status;
            builtPaused = paused;
            builtLanguage = Loc.Current;
            builtSecond = -1;
            Build(status, paused);
        }

        var second = (long)(DateTime.UtcNow - status.StartedUtc).TotalSeconds;
        if (second != builtSecond)
        {
            builtSecond = second;
            var mode = Loc.T(status.Mode == RunMode.Exact ? L.Run.ModeExact : L.Run.ModeBestMatch);
            Timing = string.Concat(mode, Separator, Loc.T(L.Run.Elapsed, Formatting.Elapsed(TimeSpan.FromSeconds(second))));
        }

        return this;
    }

    private void Build(RunStatus status, bool paused)
    {
        Title = Loc.T(L.Run.Title, status.CharacterNumber, status.CharacterCount);
        Address = status.Target is { } target ? PlotNames.Address(target) : string.Empty;
        StepLine = status.FinishingVerification ? Loc.T(L.Run.FinishingVerification) : paused ? Loc.T(L.Run.PausedLabel) : RunText.StepLine(status.Step);
        Summary = string.Concat(
            Loc.Plural(L.Run.ConfirmedEntries, status.ConfirmedEntries),
            Separator,
            Loc.T(L.Run.Deposited, Loc.Gil(status.Deposited)),
            Separator,
            Loc.T(L.Run.Remaining, Loc.Gil(status.RemainingCap)));
        Failure = status.Failure == EntryFailure.None ? string.Empty : RunText.Failure(status.Failure, status.Rejection);
        Facts = status.Target is null ? null : string.Concat(status.CharacterName, Separator, Address);
        Inspection = Loc.T(L.Run.Inspection, status.Inspection, status.InspectionLimit);
        LiveDeposit = status.LiveDeposit is { } deposit ? Loc.T(L.Run.LiveDeposit, Loc.Gil(deposit)) : string.Empty;
        var step = status.Step == EntryStep.NeedsPlayer ? Loc.T(L.Run.NeedsPlayerTitle) : StepLine;
        RunningLabel = Address.Length > 0 ? string.Concat(Address, Separator, step) : step;
        Outcome = OutcomeOf(status);
    }

    private (FontAwesomeIcon Icon, Vector4 Accent, string Title, string Body) OutcomeOf(RunStatus status)
    {
        switch (status.Step)
        {
            case EntryStep.Completed when status.Trace is { } trace:
                var body = Loc.T(L.Run.DryRunBody, PlotNames.Address(trace.Plot), Loc.Gil(trace.Deposit));
                return (FontAwesomeIcon.Flask, Styling.AccentBlue, Loc.T(L.Run.DryRunTitle), trace.GilUnchanged ? body : string.Concat(body, " ", Loc.T(L.Run.DryRunGilMoved)));
            case EntryStep.Completed:
                return (FontAwesomeIcon.CheckCircle, Styling.AccentMint, Loc.T(L.Run.EnteredTitle), Loc.T(L.Run.EnteredBody, Address, Loc.Gil(status.Deposited)));
            case EntryStep.NeedsReconciliation:
                return (FontAwesomeIcon.QuestionCircle, Styling.AccentRose, Loc.T(L.Run.UncertainTitle), Loc.T(L.Run.UncertainBody));
            case EntryStep.Stopped:
                return (FontAwesomeIcon.StopCircle, Styling.AccentAmber, Loc.T(L.Run.StoppedTitle), Failure);
            default:
                return (FontAwesomeIcon.InfoCircle, Styling.AccentAmber, Loc.T(L.Run.NoEntryTitle), Failure);
        }
    }
}
