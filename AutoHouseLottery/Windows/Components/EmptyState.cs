using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoHouseLottery.Windows.Components;

internal static class EmptyState
{
    private const float Height = 110f;
    private const float IconSize = 26f;
    private const float MessageOffset = 22f;

    public static void Draw(FontAwesomeIcon icon, string message)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(ImGui.GetContentRegionAvail().X, Height * scale);
        var origin = ImGui.GetCursorScreenPos();
        var end = origin + size;
        var drawList = ImGui.GetWindowDrawList();
        Paint.Surface(drawList, origin, end, Styling.CardRounding * scale, Styling.WithAlpha(Styling.Surface0, 0.6f), Styling.WithAlpha(Styling.BorderDim, 0.5f), topLight: false);

        var center = new Vector2((origin.X + end.X) * 0.5f, origin.Y + size.Y * 0.42f);
        ProgressRing.CenterIcon(center, icon, Styling.TextMuted, IconSize * scale);
        TextDraw.Center(message, center.X, center.Y + MessageOffset * scale, Styling.TextMuted);
        ImGui.Dummy(size);
    }
}
