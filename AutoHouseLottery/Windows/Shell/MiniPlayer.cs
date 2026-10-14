using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Core.Tasks;
using AutoHouseLottery.Windows.Components;
using AutoHouseLottery.Windows.Sections;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoHouseLottery.Windows.Shell;

internal static class MiniPlayer
{
    private const float PadX = 18f;
    private const float PadY = 14f;
    private const float ButtonSize = 34f;
    private const float ButtonGap = 8f;
    private const float BarWidth = 160f;
    private const float BarHeight = 8f;

    public static float Height()
    {
        var scale = ImGuiHelpers.GlobalScale;
        return MathF.Max(ButtonSize * scale, ImGui.GetTextLineHeight()) + PadY * 2f * scale;
    }

    public static bool Draw(Plugin plugin, Vector2 size, float windowRounding)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();
        var controller = plugin.Controller;
        var info = ReadyState.Resolve(controller);

        Dock.Background(drawList, origin, end, windowRounding);

        var padX = PadX * scale;
        var buttonSize = ButtonSize * scale;
        var buttonsWidth = buttonSize * 2f + ButtonGap * scale;
        ImGui.SetCursorScreenPos(origin);
        var hit = Hit.Area("##ahl_mini_open", new Vector2(size.X - padX - buttonsWidth - 8f * scale, size.Y));
        var hover = Motion.Hover(Motion.Key("##ahl_mini_open"), hit.Hovered);
        if (hover > 0.01f)
        {
            Paint.Fill(drawList, origin, end, Styling.WithAlpha(Styling.Surface2, 0.35f * hover), windowRounding, ImDrawFlags.RoundCornersBottom);
        }

        var midY = origin.Y + size.Y * 0.5f;
        var dotColor = controller.Paused ? info.Accent : Styling.PulseColor(info.Accent, info.AccentSoft, Styling.PulseMedium);
        Paint.Dot(drawList, new Vector2(origin.X + padX + 4f * scale, midY), 4f * scale, dotColor);

        var barWidth = BarWidth * scale;
        var barRight = end.X - padX - buttonsWidth - 16f * scale;
        var barX = barRight - barWidth;
        var barOrigin = new Vector2(barX, midY - BarHeight * scale * 0.5f);
        if (controller.Paused)
        {
            Paint.Bar(drawList, barOrigin, barWidth, BarHeight * scale, 0f, info.Accent);
        }
        else
        {
            Paint.IndeterminateBar(drawList, barOrigin, barWidth, BarHeight * scale, info.Accent);
        }

        var textX = origin.X + padX + 22f * scale;
        var label = TextDraw.Truncate(ReadyState.ShortLabel(info.Kind), barX - 16f * scale - textX);
        TextDraw.At(label, new Vector2(textX, midY - ImGui.GetTextLineHeight() * 0.5f), Styling.TextStrong);

        DrawButtons(controller, end, midY);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);
        return hit.Clicked;
    }

    private static void DrawButtons(RunController controller, Vector2 end, float midY)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var padX = PadX * scale;
        var buttonSize = ButtonSize * scale;
        var resumeBlocked = controller.PauseReason == PauseReason.InContent;
        var pausePosition = new Vector2(end.X - padX - buttonSize * 2f - ButtonGap * scale, midY - buttonSize * 0.5f);
        ImGui.SetCursorScreenPos(pausePosition);
        if (IconButton.Draw(controller.Paused ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause, "##ahl_mini_pause", buttonSize,
                controller.Paused ? Styling.AccentMintSoft : Styling.AccentAmberSoft,
                resumeBlocked ? Loc.T(L.Shell.ResumeBlocked) : controller.Paused ? Loc.T(L.Common.Resume) : Loc.T(L.Common.Pause),
                enabled: !resumeBlocked))
        {
            controller.TogglePause();
        }

        if (resumeBlocked && Hit.HoveringRect(pausePosition, pausePosition + new Vector2(buttonSize, buttonSize)))
        {
            Tooltip.Show(Loc.T(L.Shell.ResumeBlocked));
        }

        ImGui.SetCursorScreenPos(new Vector2(end.X - padX - buttonSize, midY - buttonSize * 0.5f));
        if (IconButton.Draw(FontAwesomeIcon.Stop, "##ahl_mini_stop", buttonSize, Styling.AccentRose, Loc.T(L.Common.StopRun)))
        {
            controller.Stop();
        }
    }
}
