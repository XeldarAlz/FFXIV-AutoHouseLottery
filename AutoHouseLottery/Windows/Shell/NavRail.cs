using AutoHouseLottery.Core;
using AutoHouseLottery.Core.External;
using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Windows.Components;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace AutoHouseLottery.Windows.Shell;

internal static class NavRail
{
    private readonly record struct Entry(AppWindow.Page Page, FontAwesomeIcon Icon, string Id, LocString Label);

    private const float TopPad = 12f;
    private const float Gap = 8f;

    private static readonly Entry[] entries =
    [
        new(AppWindow.Page.FindHome,  FontAwesomeIcon.MapMarkedAlt,  "##ahl_nav_find",      L.Shell.NavFindHome),
        new(AppWindow.Page.MyLottery, FontAwesomeIcon.Ticket,        "##ahl_nav_lottery",   L.Shell.NavMyLottery),
        new(AppWindow.Page.Saved,     FontAwesomeIcon.Bookmark,      "##ahl_nav_saved",     L.Shell.NavSaved),
        new(AppWindow.Page.Settings,  FontAwesomeIcon.Cog,           "##ahl_nav_settings",  L.Shell.NavSettings),
        new(AppWindow.Page.Plugins,   FontAwesomeIcon.Plug,          "##ahl_nav_plugins",   L.Shell.NavPlugins),
        new(AppWindow.Page.Log,       FontAwesomeIcon.Terminal,      "##ahl_nav_log",       L.Shell.NavLog),
        new(AppWindow.Page.Changelog, FontAwesomeIcon.Newspaper,     "##ahl_nav_changelog", L.Shell.NavChangelog),
        new(AppWindow.Page.About,     FontAwesomeIcon.InfoCircle,    "##ahl_nav_about",     L.Shell.NavAbout),
    ];

    public static AppWindow.Page? Draw(AppWindow.Page current, Plugin plugin)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var button = Layout.RailButton * scale;
        var gap = Gap * scale;
        var railOrigin = ImGui.GetCursorScreenPos();
        var available = ImGui.GetContentRegionAvail().X;
        var x = railOrigin.X + (available - button) * 0.5f;
        var startY = railOrigin.Y + TopPad * scale;
        var drawList = ImGui.GetWindowDrawList();

        var selectedIndex = 0;
        for (var index = 0; index < entries.Length; index++)
        {
            if (entries[index].Page == current)
            {
                selectedIndex = index;
            }
        }

        var indicator = Motion.Approach(Motion.Key("##ahl_rail_indicator"), selectedIndex, 16f);
        var indicatorY = startY + (button + gap) * indicator;
        var indicatorMin = new Vector2(x, indicatorY);
        var indicatorMax = indicatorMin + new Vector2(button, button);
        Paint.Glass(drawList, indicatorMin, indicatorMax, 12f * scale, Styling.AccentGlow, 0.30f);
        Paint.Fill(drawList, new Vector2(railOrigin.X, indicatorY + button * 0.25f), new Vector2(railOrigin.X + 3f * scale, indicatorY + button * 0.75f),
            Styling.AccentGlow, 2f * scale);

        var missingPlugins = !ExternalPlugins.AllRequiredInstalled();
        var running = plugin.Controller.Running;
        var unseenChangelog = plugin.Configuration.HasUnseenChangelog;
        AppWindow.Page? clicked = null;

        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            var y = startY + (button + gap) * index;
            ImGui.SetCursorScreenPos(new Vector2(x, y));
            var hit = Hit.Area(entry.Id, new Vector2(button, button));
            var hover = Motion.Hover(Motion.Key(entry.Id), hit.Hovered);
            var selected = index == selectedIndex;

            if (!selected && hover > 0.01f)
            {
                Paint.Fill(drawList, new Vector2(x, y), new Vector2(x + button, y + button), Styling.WithAlpha(Styling.Surface2, 0.8f * hover), 12f * scale);
            }

            var center = new Vector2(x + button * 0.5f, y + button * 0.5f);
            var color = selected ? Styling.TextStrong : Vector4.Lerp(Styling.TextDim, Styling.TextSecondary, hover);
            TextDraw.IconCentered(entry.Icon, center, color);

            DrawBadge(drawList, entry.Page, current, center, button, missingPlugins, running, unseenChangelog);

            if (hit.Hovered)
            {
                Tooltip.Show(Loc.T(entry.Label));
            }

            if (hit.Clicked)
            {
                clicked = entry.Page;
            }
        }

        ImGui.SetCursorScreenPos(railOrigin);
        ImGui.Dummy(new Vector2(available, TopPad * scale + (button + gap) * entries.Length));
        return clicked;
    }

    private static void DrawBadge(ImDrawListPtr drawList, AppWindow.Page page, AppWindow.Page current, Vector2 center, float button, bool missingPlugins, bool running, bool unseenChangelog)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var badgeCenter = center + new Vector2(button * 0.30f, -button * 0.30f);
        var radius = 3.5f * scale;

        if (page == AppWindow.Page.Plugins && missingPlugins)
        {
            drawList.AddCircleFilled(badgeCenter, radius + 1.5f * scale, Paint.Col(Styling.WindowBg));
            drawList.AddCircleFilled(badgeCenter, radius, Paint.Col(Styling.AccentRose));
        }
        else if (page == AppWindow.RunPage && running)
        {
            drawList.AddCircleFilled(badgeCenter, radius + 1.5f * scale, Paint.Col(Styling.WindowBg));
            drawList.AddCircleFilled(badgeCenter, radius, Paint.Col(Styling.PulseColor(Styling.AccentBlue, Styling.AccentBlueSoft, Styling.PulseMedium)));
        }
        else if (page == AppWindow.Page.Changelog && current != AppWindow.Page.Changelog && unseenChangelog)
        {
            drawList.AddCircleFilled(badgeCenter, radius + 1.5f * scale, Paint.Col(Styling.WindowBg));
            drawList.AddCircleFilled(badgeCenter, radius, Paint.Col(Styling.PulseColor(Styling.AccentGlow, Styling.AccentGlowSoft, Styling.PulseMedium)));
        }
        else if (page == AppWindow.Page.Log && current != AppWindow.Page.Log && RunLog.Unseen is { } unseen)
        {
            drawList.AddCircleFilled(badgeCenter, radius + 1.5f * scale, Paint.Col(Styling.WindowBg));
            drawList.AddCircleFilled(badgeCenter, radius, Paint.Col(unseen == RunLogLevel.Error ? Styling.AccentRose : Styling.AccentAmber));
        }
    }
}
