using AutoHouseLottery.Core.Integration;
using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Core.Tasks;
using AutoHouseLottery.Core.Tasks.Model;
using AutoHouseLottery.Windows.Components;
using AutoHouseLottery.Windows.Shell;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoHouseLottery.Windows.Sections;

// The active run (UX section 9): who and where, the plain step, what is deposited, Pause and Stop. A run that waits
// for the player, or has ended, shows one card with what happened and the action that fits.
internal static class RunPanel
{
    private const float PadX = 18f;
    private const float PadY = 14f;
    private const float LineGap = 6f;
    private const float ButtonGap = 8f;
    private const float SectionGap = 12f;
    private const float GlassTint = 0.08f;
    private const string Separator = " · ";
    private const int SingleCharacter = 1;
    private const int FixedLines = 3;
    private const int AlwaysShownDetailLines = 1;

    private static bool detailsOpen;

    public static bool Visible(RunController controller) => controller.Busy || controller.Status.RunId != Guid.Empty;

    public static void Draw(Plugin plugin)
    {
        var controller = plugin.Controller;
        var status = controller.Status;
        if (controller.Reconciling && !controller.Running)
        {
            DrawReconciling();
        }
        else if (status.Step == EntryStep.NeedsPlayer && controller.Running)
        {
            DrawNeedsPlayer(controller, status);
        }
        else if (controller.Running)
        {
            DrawActive(controller, status);
        }
        else if (status.RunId != Guid.Empty)
        {
            DrawFinished(plugin, status);
        }

        Styling.VSpace(SectionGap);
    }

    private static void DrawActive(RunController controller, RunStatus status)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var lineHeight = ImGui.GetTextLineHeight();
        var detailLines = detailsOpen ? DetailLineCount(status) : 0;
        var lines = FixedLines + detailLines;
        var height = PadY * 2f * scale + (lineHeight + LineGap * scale) * lines + Layout.ActionPillHeight * scale;
        var end = origin + new Vector2(width, height);
        var drawList = ImGui.GetWindowDrawList();
        var accent = controller.Paused ? Styling.AccentAmber : status.FinishingVerification ? Styling.AccentMint : Styling.AccentBlue;
        Paint.Glass(drawList, origin, end, Styling.CardRounding * scale, accent, GlassTint);

        var x = origin.X + PadX * scale;
        var right = end.X - PadX * scale;
        var y = origin.Y + PadY * scale;
        var titleX = DrawModeBadge(drawList, status.EntryMode, x, y + lineHeight * 0.5f);
        TextDraw.At(TextDraw.Truncate(Loc.T(L.Run.Title, SingleCharacter, SingleCharacter), right - titleX), new Vector2(titleX, y), Styling.TextStrong);
        y += lineHeight + LineGap * scale;

        var address = status.Target is { } target ? PlotNames.Address(target) : string.Empty;
        var stepLine = StepLine(controller, status);
        var stepWidth = TextDraw.Measure(stepLine).X;
        TextDraw.At(TextDraw.Truncate(address, right - x - stepWidth - ButtonGap * scale), new Vector2(x, y), Styling.TextSecondary);
        TextDraw.Right(stepLine, right, y, status.FinishingVerification ? Styling.PulseColor(Styling.AccentMint, Styling.AccentMintSoft) : accent);
        y += lineHeight + LineGap * scale;

        TextDraw.At(TextDraw.Truncate(Summary(status), right - x), new Vector2(x, y), Styling.TextDim);
        y += lineHeight + LineGap * scale;

        if (detailsOpen)
        {
            var mode = Loc.T(status.Mode == RunMode.Exact ? L.Run.ModeExact : L.Run.ModeBestMatch);
            y = DetailLine(string.Concat(mode, Separator, Loc.T(L.Run.Elapsed, Formatting.Elapsed(DateTime.UtcNow - status.StartedUtc))), x, y, right);
            if (status.Mode == RunMode.BestMatch)
            {
                y = DetailLine(Loc.T(L.Run.Inspection, status.Inspection, status.InspectionLimit), x, y, right);
            }

            if (status.LiveDeposit is { } deposit)
            {
                y = DetailLine(Loc.T(L.Run.LiveDeposit, Loc.Gil(deposit)), x, y, right);
            }
        }

        DrawControls(controller, status, new Vector2(x, y), right);
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
    }

    private static void DrawControls(RunController controller, RunStatus status, Vector2 at, float right)
    {
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.SetCursorScreenPos(at);
        var verifying = status.FinishingVerification;
        var pauseLabel = controller.Paused ? Loc.T(L.Common.Resume) : Loc.T(L.Common.Pause);
        var pauseTooltip = verifying ? Loc.T(L.Run.FinishingVerification) : controller.PauseReason == PauseReason.InContent ? Loc.T(L.Shell.ResumeBlocked) : null;
        if (PillButton.Draw("##ahl_run_pause", pauseLabel, controller.Paused ? Styling.AccentMint : Styling.AccentAmber, PillButton.Emphasis.Tinted,
                controller.Paused ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause, !verifying && controller.PauseReason != PauseReason.InContent, Layout.ActionPillHeight, pauseTooltip))
        {
            controller.TogglePause();
        }

        ImGui.SetCursorScreenPos(new Vector2(at.X + PillButton.Width(pauseLabel, FontAwesomeIcon.Pause) + ButtonGap * scale, at.Y));
        if (PillButton.Draw("##ahl_run_stop", Loc.T(L.Common.StopRun), Styling.AccentRose, PillButton.Emphasis.Ghost, FontAwesomeIcon.Stop, !verifying, Layout.ActionPillHeight,
                verifying ? Loc.T(L.Run.FinishingVerification) : null))
        {
            controller.Stop();
        }

        var detailsLabel = Loc.T(detailsOpen ? L.Run.HideDetails : L.Run.ShowDetails);
        ImGui.SetCursorScreenPos(new Vector2(right - PillButton.Width(detailsLabel, FontAwesomeIcon.InfoCircle), at.Y));
        if (PillButton.Draw("##ahl_run_details", detailsLabel, Styling.AccentNebula, PillButton.Emphasis.Ghost, FontAwesomeIcon.InfoCircle, true, Layout.ActionPillHeight))
        {
            detailsOpen = !detailsOpen;
        }
    }

    private static void DrawNeedsPlayer(RunController controller, RunStatus status)
    {
        var travel = status.Failure is EntryFailure.TravelFailed or EntryFailure.DependencyBusy or EntryFailure.WrongDestination;
        var manual = travel || status.Failure is EntryFailure.TravelUnavailable or EntryFailure.PlacardNotFound or EntryFailure.PlacardNotOpened;
        var card = NoticeCard.Begin(FontAwesomeIcon.HandPaper, Loc.T(L.Run.NeedsPlayerTitle), RunText.Failure(status.Failure, status.Rejection), Facts(status), Styling.AccentAmber, hasButtons: true);
        if (travel && NoticeCard.Button(ref card, "##ahl_run_retry_travel", Loc.T(L.Run.RetryTravel), Styling.AccentMint, PillButton.Emphasis.Filled, FontAwesomeIcon.Route))
        {
            controller.Resume();
        }
        else if (!travel && NoticeCard.Button(ref card, "##ahl_run_resume", Loc.T(L.Common.Resume), Styling.AccentMint, PillButton.Emphasis.Filled, FontAwesomeIcon.Play))
        {
            controller.Resume();
        }

        if (manual && NoticeCard.Button(ref card, "##ahl_run_manual", Loc.T(L.Run.VisitManually), Styling.AccentBlue, PillButton.Emphasis.Tinted, FontAwesomeIcon.Walking))
        {
            controller.VisitManually();
        }

        if (NoticeCard.Button(ref card, "##ahl_run_stop_card", Loc.T(L.Common.StopRun), Styling.AccentRose, PillButton.Emphasis.Ghost, FontAwesomeIcon.Stop))
        {
            controller.Stop();
        }

        NoticeCard.End(card);
    }

    private static void DrawFinished(Plugin plugin, RunStatus status)
    {
        var controller = plugin.Controller;
        var (icon, accent, title, body) = Outcome(status);
        var card = NoticeCard.Begin(icon, title, body, Facts(status), accent, hasButtons: true);
        switch (status.Step)
        {
            case EntryStep.NeedsReconciliation:
                if (NoticeCard.Button(ref card, "##ahl_run_check_again", Loc.T(L.Run.CheckAgain), Styling.AccentMint, PillButton.Emphasis.Filled, FontAwesomeIcon.Sync, !controller.Busy))
                {
                    controller.Reconcile();
                }

                break;
            case EntryStep.CompletedPartial when FallbackRules.IsConclusivePreCommit(status.Failure) || status.Failure == EntryFailure.InspectionsExhausted:
                if (NoticeCard.Button(ref card, "##ahl_run_review", Loc.T(L.Run.ReviewChanges), Styling.AccentBlue, PillButton.Emphasis.Tinted, FontAwesomeIcon.Search))
                {
                    controller.Dismiss();
                    plugin.ShowPage(AppWindow.Page.FindHome);
                }

                break;
            case EntryStep.Completed or EntryStep.CompletedPartial:
                if (NoticeCard.Button(ref card, "##ahl_run_my_lottery", Loc.T(L.Run.OpenMyLottery), Styling.AccentBlue, PillButton.Emphasis.Tinted, FontAwesomeIcon.Ticket))
                {
                    plugin.ShowPage(AppWindow.Page.MyLottery);
                }

                break;
        }

        if (status.Step != EntryStep.NeedsReconciliation && NoticeCard.Button(ref card, "##ahl_run_dismiss", Loc.T(L.Run.Dismiss), Styling.AccentNebula, PillButton.Emphasis.Ghost, FontAwesomeIcon.Times))
        {
            controller.Dismiss();
        }

        NoticeCard.End(card);
    }

    private static void DrawReconciling()
    {
        var card = NoticeCard.Begin(FontAwesomeIcon.Sync, Loc.T(L.Run.Checking), Loc.T(L.Run.UncertainBody), null, Styling.AccentBlue, hasButtons: false);
        NoticeCard.End(card);
    }

    private static (FontAwesomeIcon Icon, Vector4 Accent, string Title, string Body) Outcome(RunStatus status)
    {
        switch (status.Step)
        {
            case EntryStep.Completed when status.Trace is { } trace:
                var body = Loc.T(L.Run.DryRunBody, PlotNames.Address(trace.Plot), Loc.Gil(trace.Deposit));
                return (FontAwesomeIcon.Flask, Styling.AccentBlue, Loc.T(L.Run.DryRunTitle), trace.GilUnchanged ? body : string.Concat(body, " ", Loc.T(L.Run.DryRunGilMoved)));
            case EntryStep.Completed:
                var address = status.Target is { } target ? PlotNames.Address(target) : string.Empty;
                return (FontAwesomeIcon.CheckCircle, Styling.AccentMint, Loc.T(L.Run.EnteredTitle), Loc.T(L.Run.EnteredBody, address, Loc.Gil(status.Deposited)));
            case EntryStep.NeedsReconciliation:
                return (FontAwesomeIcon.QuestionCircle, Styling.AccentRose, Loc.T(L.Run.UncertainTitle), Loc.T(L.Run.UncertainBody));
            case EntryStep.Stopped:
                return (FontAwesomeIcon.StopCircle, Styling.AccentAmber, Loc.T(L.Run.StoppedTitle), status.Failure == EntryFailure.None ? string.Empty : RunText.Failure(status.Failure, status.Rejection));
            default:
                return (FontAwesomeIcon.InfoCircle, Styling.AccentAmber, Loc.T(L.Run.NoEntryTitle), RunText.Failure(status.Failure, status.Rejection));
        }
    }

    private static string? Facts(RunStatus status) => status.Target is { } target ? string.Concat(status.CharacterName, Separator, PlotNames.Address(target)) : null;

    private static float DrawModeBadge(ImDrawListPtr drawList, EntryMode mode, float x, float midY)
    {
        var dryRun = mode == EntryMode.DryRun;
        var width = Badge.DrawLeft(drawList, Loc.T(dryRun ? L.Run.DryRunBadge : L.Run.RealEntryBadge), dryRun ? Styling.AccentBlue : Styling.AccentRose, x, midY);
        return x + width + ButtonGap * ImGuiHelpers.GlobalScale;
    }

    public static string StepLine(RunController controller, RunStatus status)
    {
        if (status.FinishingVerification)
        {
            return Loc.T(L.Run.FinishingVerification);
        }

        return controller.Paused ? Loc.T(L.Run.PausedLabel) : RunText.StepLine(status.Step);
    }

    private static string Summary(RunStatus status)
        => string.Concat(
            Loc.Plural(L.Run.ConfirmedEntries, status.ConfirmedEntries),
            Separator,
            Loc.T(L.Run.Deposited, Loc.Gil(status.Deposited)),
            Separator,
            Loc.T(L.Run.Remaining, Loc.Gil(status.RemainingCap)));

    private static int DetailLineCount(RunStatus status)
        => AlwaysShownDetailLines + (status.Mode == RunMode.BestMatch ? 1 : 0) + (status.LiveDeposit is null ? 0 : 1);

    private static float DetailLine(string text, float x, float y, float right)
    {
        TextDraw.At(TextDraw.Truncate(text, right - x), new Vector2(x, y), Styling.TextMuted);
        return y + ImGui.GetTextLineHeight() + LineGap * ImGuiHelpers.GlobalScale;
    }
}
