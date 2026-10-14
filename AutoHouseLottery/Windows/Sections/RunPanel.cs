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
    private const int FixedLines = 3;
    private const int AlwaysShownDetailLines = 1;
    private const float MinimumAddressWidth = 160f;

    private static bool detailsOpen;

    public static bool Visible(RunController controller) => controller.Busy || controller.Status.RunId != Guid.Empty || SettlementPanel.Visible(controller);

    // On the run's own page the dock below holds Pause and Stop, so the card leaves them out and keeps the page's room
    // for the results.
    public static void Draw(Plugin plugin, bool controlsInDock)
    {
        var controller = plugin.Controller;
        var status = controller.Status;
        if (SettlementPanel.Visible(controller) && !controller.Running)
        {
            SettlementPanel.Draw(plugin);
            Styling.VSpace(SectionGap);
        }

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
            DrawActive(controller, status, controlsInDock);
        }
        else if (status.RunId != Guid.Empty)
        {
            DrawFinished(plugin, status);
        }

        Styling.VSpace(SectionGap);
    }

    private static void DrawActive(RunController controller, RunStatus status, bool controlsInDock)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var lineHeight = ImGui.GetTextLineHeight();
        var detailLines = detailsOpen ? DetailLineCount(status) : 0;
        var lines = FixedLines + detailLines;
        var height = PadY * 2f * scale + (lineHeight + LineGap * scale) * lines + (controlsInDock ? 0f : Layout.ActionPillHeight * scale);
        var end = origin + new Vector2(width, height);
        var drawList = ImGui.GetWindowDrawList();
        var accent = controller.Paused ? Styling.AccentAmber : status.FinishingVerification ? Styling.AccentMint : Styling.AccentBlue;
        Paint.Glass(drawList, origin, end, Styling.CardRounding * scale, accent, GlassTint);

        var x = origin.X + PadX * scale;
        var right = end.X - PadX * scale;
        var y = origin.Y + PadY * scale;
        var text = RunPanelText.Shared.For(controller);
        var titleX = DrawModeBadge(drawList, status.EntryMode, x, y + lineHeight * 0.5f);
        var titleRight = right;
        if (controlsInDock)
        {
            titleRight -= lineHeight + ButtonGap * scale;
            DrawDetailsToggle(new Vector2(titleRight + ButtonGap * scale, y), lineHeight);
        }

        TextDraw.At(TextDraw.Truncate(text.Title, titleRight - titleX), new Vector2(titleX, y), Styling.TextStrong);
        y += lineHeight + LineGap * scale;

        DrawAddressAndStep(text, status, accent, x, y, right);
        y += lineHeight + LineGap * scale;

        TextDraw.At(TextDraw.Truncate(text.Summary, right - x), new Vector2(x, y), Styling.TextDim);
        y += lineHeight + LineGap * scale;

        if (detailsOpen)
        {
            y = DetailLine(text.Timing, x, y, right);
            if (status.Mode == RunMode.BestMatch)
            {
                y = DetailLine(text.Inspection, x, y, right);
            }

            if (status.LiveDeposit is not null)
            {
                y = DetailLine(text.LiveDeposit, x, y, right);
            }
        }

        if (!controlsInDock)
        {
            DrawControls(controller, status, new Vector2(x, y), right);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
    }

    // The address keeps a readable width; the step gives way first when both do not fit.
    private static void DrawAddressAndStep(RunPanelText text, RunStatus status, Vector4 accent, float x, float y, float right)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var gap = ButtonGap * scale;
        var available = right - x;
        var addressRoom = MathF.Max(available - TextDraw.Measure(text.StepLine).X - gap, MathF.Min(MinimumAddressWidth * scale, available * 0.5f));
        var address = TextDraw.Truncate(text.Address, addressRoom);
        var step = TextDraw.Truncate(text.StepLine, MathF.Max(1f, available - TextDraw.Measure(address).X - gap));
        TextDraw.At(address, new Vector2(x, y), Styling.TextSecondary);
        TextDraw.Right(step, right, y, status.FinishingVerification ? Styling.PulseColor(Styling.AccentMint, Styling.AccentMintSoft) : accent);
    }

    private static void DrawDetailsToggle(Vector2 at, float size)
    {
        ImGui.SetCursorScreenPos(at);
        if (IconButton.Draw(FontAwesomeIcon.InfoCircle, "##ahl_run_details_icon", size, Styling.AccentNebula, Loc.T(detailsOpen ? L.Run.HideDetails : L.Run.ShowDetails)))
        {
            detailsOpen = !detailsOpen;
        }
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

        var stopX = at.X + PillButton.Width(pauseLabel, FontAwesomeIcon.Pause) + ButtonGap * scale;
        var stopLabel = Loc.T(L.Common.StopRun);
        ImGui.SetCursorScreenPos(new Vector2(stopX, at.Y));
        if (PillButton.Draw("##ahl_run_stop", stopLabel, Styling.AccentRose, PillButton.Emphasis.Ghost, FontAwesomeIcon.Stop, !verifying, Layout.ActionPillHeight,
                verifying ? Loc.T(L.Run.FinishingVerification) : null))
        {
            controller.Stop();
        }

        // Details never covers Stop: when the row has no room left for it, the button is left out.
        var detailsLabel = Loc.T(detailsOpen ? L.Run.HideDetails : L.Run.ShowDetails);
        var detailsWidth = PillButton.Width(detailsLabel, FontAwesomeIcon.InfoCircle);
        var detailsX = right - detailsWidth;
        if (detailsX < stopX + PillButton.Width(stopLabel, FontAwesomeIcon.Stop) + ButtonGap * scale)
        {
            return;
        }

        ImGui.SetCursorScreenPos(new Vector2(detailsX, at.Y));
        if (PillButton.Draw("##ahl_run_details", detailsLabel, Styling.AccentNebula, PillButton.Emphasis.Ghost, FontAwesomeIcon.InfoCircle, true, Layout.ActionPillHeight))
        {
            detailsOpen = !detailsOpen;
        }
    }

    private static void DrawNeedsPlayer(RunController controller, RunStatus status)
    {
        var travel = status.Failure is EntryFailure.TravelFailed or EntryFailure.DependencyBusy or EntryFailure.WrongDestination;
        var manual = travel || status.Failure is EntryFailure.TravelUnavailable or EntryFailure.PlacardNotFound or EntryFailure.PlacardNotOpened;
        var text = RunPanelText.Shared.For(controller);
        var resumeLabel = Loc.T(travel ? L.Run.RetryTravel : L.Common.Resume);
        var resumeIcon = travel ? FontAwesomeIcon.Route : FontAwesomeIcon.Play;
        var manualLabel = Loc.T(L.Run.VisitManually);
        var stopLabel = Loc.T(L.Common.StopRun);
        Span<float> widths = [NoticeCard.Width(resumeLabel, resumeIcon), NoticeCard.Width(manualLabel, FontAwesomeIcon.Walking)];
        var card = NoticeCard.Begin(FontAwesomeIcon.HandPaper, Loc.T(L.Run.NeedsPlayerTitle), text.Failure, text.Facts, Styling.AccentAmber, hasButtons: true,
            manual ? widths : widths[..1], NoticeCard.Width(stopLabel, FontAwesomeIcon.Stop));
        if (NoticeCard.Button(ref card, travel ? "##ahl_run_retry_travel" : "##ahl_run_resume", resumeLabel, Styling.AccentMint, PillButton.Emphasis.Filled, resumeIcon))
        {
            controller.Resume();
        }

        if (manual && NoticeCard.Button(ref card, "##ahl_run_manual", manualLabel, Styling.AccentBlue, PillButton.Emphasis.Tinted, FontAwesomeIcon.Walking))
        {
            controller.VisitManually();
        }

        if (NoticeCard.TrailingButton(card, "##ahl_run_stop_card", stopLabel, Styling.AccentRose, PillButton.Emphasis.Ghost, FontAwesomeIcon.Stop))
        {
            controller.Stop();
        }

        NoticeCard.End(card);
    }

    private static void DrawFinished(Plugin plugin, RunStatus status)
    {
        var controller = plugin.Controller;
        var text = RunPanelText.Shared.For(controller);
        var (icon, accent, title, body) = text.Outcome;
        var card = NoticeCard.Begin(icon, title, body, text.Facts, accent, hasButtons: true);
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

    private static float DrawModeBadge(ImDrawListPtr drawList, EntryMode mode, float x, float midY)
    {
        var dryRun = mode == EntryMode.DryRun;
        var width = Badge.DrawLeft(drawList, Loc.T(dryRun ? L.Run.DryRunBadge : L.Run.RealEntryBadge), dryRun ? Styling.AccentBlue : Styling.AccentRose, x, midY);
        return x + width + ButtonGap * ImGuiHelpers.GlobalScale;
    }

    private static int DetailLineCount(RunStatus status)
        => AlwaysShownDetailLines + (status.Mode == RunMode.BestMatch ? 1 : 0) + (status.LiveDeposit is null ? 0 : 1);

    private static float DetailLine(string text, float x, float y, float right)
    {
        TextDraw.At(TextDraw.Truncate(text, right - x), new Vector2(x, y), Styling.TextMuted);
        return y + ImGui.GetTextLineHeight() + LineGap * ImGuiHelpers.GlobalScale;
    }
}
