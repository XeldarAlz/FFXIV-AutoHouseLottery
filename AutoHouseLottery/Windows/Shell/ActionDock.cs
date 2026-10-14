using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Core.Tasks;
using AutoHouseLottery.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoHouseLottery.Windows.Shell;

internal static class ActionDock
{
    private const float PadX = 18f;
    private const float ButtonGap = 8f;

    public static void Draw(Plugin plugin, Vector2 size, float windowRounding)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();
        Dock.Background(drawList, origin, end, windowRounding);

        var padX = PadX * scale;
        var buttonHeight = Layout.HeroButtonHeight * scale;
        var innerWidth = size.X - padX * 2f;
        ImGui.SetCursorScreenPos(new Vector2(origin.X + padX, origin.Y + (size.Y - buttonHeight) * 0.5f));
        DrawRunControls(plugin.Controller, innerWidth);

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);
    }

    private static void DrawRunControls(RunController controller, float innerWidth)
    {
        var gap = ButtonGap * ImGuiHelpers.GlobalScale;
        var half = (innerWidth - gap) * 0.5f;
        if (PauseButton.Draw(controller.PauseReason, half))
        {
            controller.TogglePause();
        }

        ImGui.SameLine(0f, gap);
        var state = controller.Paused ? Loc.T(L.Dock.StatePaused) : Loc.T(L.Dock.StateRunning);
        if (StopButton.Draw(state, half))
        {
            controller.Stop();
        }
    }
}
