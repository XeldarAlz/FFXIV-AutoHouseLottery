using AutoHouseLottery.Core.Find.Model;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoHouseLottery.Windows.Find;

// Every plot status has both its own color and its own shape, so the map and the list read the same without color.
internal static class StatusMarks
{
    private const float StrokeWidth = 1.6f;
    private const float RingWidth = 2.2f;
    private const float RingGap = 3f;
    private const float TriangleLift = 0.15f;
    private const float BeaconPlateScale = 1.45f;
    private const float BeaconHaloScale = 1.9f;
    private const float BeaconHaloGrowth = 0.7f;
    private const float BeaconHaloAlpha = 0.45f;
    private const float BeaconShadowDrop = 2f;
    private const float BeaconShadowAlpha = 0.5f;
    private const float BeaconRimAlpha = 0.95f;
    private const float BeaconPlateAlpha = 0.92f;

    public static Vector4 Color(PlotStatus? status) => status switch
    {
        PlotStatus.EntryOpen => Styling.AccentMint,
        PlotStatus.Results => Styling.AccentBlue,
        PlotStatus.NextCycle => Styling.AccentAmber,
        PlotStatus.ForSale => Styling.AccentRose,
        PlotStatus.NeedsRefresh => Styling.AccentAmberSoft,
        _ => Styling.TextMuted,
    };

    public static void Draw(ImDrawListPtr drawList, Vector2 center, float radius, PlotStatus? status, float alpha = 1f)
    {
        var color = Paint.Col(Styling.WithAlpha(Color(status), alpha));
        var fill = Paint.Col(Styling.WithAlpha(Styling.Darken(Color(status), 0.55f), 0.85f * alpha));
        var stroke = StrokeWidth * ImGuiHelpers.GlobalScale;
        switch (status)
        {
            case PlotStatus.EntryOpen:
                drawList.AddCircleFilled(center, radius, color);
                break;
            case PlotStatus.Results:
                Diamond(drawList, center, radius * 1.15f, color);
                break;
            case PlotStatus.NextCycle:
                drawList.AddRectFilled(center - new Vector2(radius * 0.85f), center + new Vector2(radius * 0.85f), color, radius * 0.2f);
                break;
            case PlotStatus.ForSale:
                Triangle(drawList, center, radius * 1.15f, color);
                break;
            case PlotStatus.NeedsRefresh:
                drawList.AddRectFilled(center - new Vector2(radius * 0.8f), center + new Vector2(radius * 0.8f), fill, radius * 0.2f);
                drawList.AddRect(center - new Vector2(radius * 0.8f), center + new Vector2(radius * 0.8f), color, radius * 0.2f, ImDrawFlags.None, stroke);
                break;
            case PlotStatus.Occupied:
                Cross(drawList, center, radius * 0.7f, color, stroke);
                break;
            case PlotStatus.NotReported:
                drawList.AddCircleFilled(center, radius * 0.9f, fill);
                drawList.AddCircle(center, radius * 0.9f, color, 0, stroke);
                break;
            default:
                drawList.AddCircleFilled(center, radius * 0.35f, color);
                break;
        }
    }

    // The plate a matching plot's mark sits on, so it stands out from the light map and its printed plot dots at any zoom:
    // a pulsing halo in the status color, a drop shadow, and a dark disc with a light rim.
    public static void Beacon(ImDrawListPtr drawList, Vector2 center, float radius, PlotStatus? status)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var pulse = Styling.Pulse(Styling.PulseBreath);
        var plate = BeaconRadius(radius);
        drawList.AddCircleFilled(center, radius * (BeaconHaloScale + BeaconHaloGrowth * pulse), Paint.Col(Styling.WithAlpha(Color(status), BeaconHaloAlpha * (1f - pulse))));
        drawList.AddCircleFilled(center + new Vector2(0f, BeaconShadowDrop * scale), plate, Paint.Col(new Vector4(0f, 0f, 0f, BeaconShadowAlpha)));
        drawList.AddCircleFilled(center, plate, Paint.Col(Styling.WithAlpha(Styling.Surface0, BeaconPlateAlpha)));
        drawList.AddCircle(center, plate, Paint.Col(Styling.WithAlpha(Styling.TextStrong, BeaconRimAlpha)), 0, RingWidth * scale);
    }

    public static float BeaconRadius(float radius) => radius * BeaconPlateScale;

    public static void Shortlisted(ImDrawListPtr drawList, Vector2 center, float radius, float alpha = 1f)
    {
        var scale = ImGuiHelpers.GlobalScale;
        drawList.AddCircle(center, radius + RingGap * scale, Paint.Col(Styling.WithAlpha(Styling.AccentGlow, alpha)), 0, RingWidth * scale);
    }

    private static void Diamond(ImDrawListPtr drawList, Vector2 center, float radius, uint color)
    {
        drawList.AddQuadFilled(center - new Vector2(0f, radius), center + new Vector2(radius, 0f), center + new Vector2(0f, radius), center - new Vector2(radius, 0f), color);
    }

    private static void Triangle(ImDrawListPtr drawList, Vector2 center, float radius, uint color)
    {
        var lifted = center + new Vector2(0f, radius * TriangleLift);
        drawList.AddTriangleFilled(
            lifted - new Vector2(0f, radius),
            lifted + new Vector2(radius * 0.9f, radius * 0.6f),
            lifted + new Vector2(-radius * 0.9f, radius * 0.6f),
            color);
    }

    private static void Cross(ImDrawListPtr drawList, Vector2 center, float half, uint color, float stroke)
    {
        drawList.AddLine(center - new Vector2(half, half), center + new Vector2(half, half), color, stroke);
        drawList.AddLine(center + new Vector2(-half, half), center + new Vector2(half, -half), color, stroke);
    }
}
