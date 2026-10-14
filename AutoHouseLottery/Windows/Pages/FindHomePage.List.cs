using AutoHouseLottery.Core.Find;
using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Windows.Components;
using AutoHouseLottery.Windows.Find;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoHouseLottery.Windows.Pages;

internal sealed partial class FindHomePage
{
    private const float RowPadY = 6f;
    private const float CellPad = 8f;
    private const float CheckColumn = 30f;
    private const float CheckBox = 15f;
    private const float SizeColumn = 72f;
    private const float PriceColumn = 118f;
    private const float EntriesColumn = 68f;
    private const float PhaseColumn = 128f;
    private const float ObservedColumn = 92f;
    private const float StatusMarkRadius = 5f;
    private const float NarrowObserved = 600f;
    private const float NarrowSize = 500f;
    private const float MinimumAddressColumn = 170f;
    private const float MinimumAddressFallback = 80f;
    private const string ListId = "##ahl_find_list";

    private readonly Dictionary<PlotKey, RowText> rowTexts = [];
    private (FindResults? Results, long Minute, LanguageInfo? Language) rowTextsFor;

    private readonly record struct RowText(string Size, string Price, string Entries, string Phase, string Observed);

    // At the smallest widths the entry count goes first, then the phase keeps only its status mark (the row's tooltip
    // still names it), so the address column never drops below a readable width while there is room for it.
    private readonly record struct ListColumns(float Address, float Size, float Price, float Entries, float Phase, float Observed, bool ShowSize, bool ShowObserved, bool ShowEntries, bool PhaseText);

    private void DrawList(FindView view, DateTime now)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var width = ImGui.GetContentRegionAvail().X;
        var columns = Columns(width, scale);
        DrawListHeader(columns, width);
        using var child = ImRaii.Child(ListId, ImGui.GetContentRegionAvail(), false, ImGuiWindowFlags.NoBackground);
        if (!child)
        {
            return;
        }

        RefreshRowTexts(now);
        var rows = shownResults.Rows;
        var rowHeight = TextDraw.LineHeight() + RowPadY * 2f * scale;
        var scrollY = ImGui.GetScrollY();
        var first = Math.Clamp((int)(scrollY / rowHeight) - 1, 0, rows.Length);
        var last = Math.Min(rows.Length, first + (int)(ImGui.GetWindowHeight() / rowHeight) + 3);
        var contentOrigin = ImGui.GetCursorScreenPos();
        var innerWidth = ImGui.GetContentRegionAvail().X;
        for (var index = first; index < last; index++)
        {
            ImGui.SetCursorScreenPos(contentOrigin + new Vector2(0f, index * rowHeight));
            DrawRow(rows[index], index, columns, innerWidth, rowHeight, now);
        }

        ImGui.SetCursorScreenPos(contentOrigin);
        ImGui.Dummy(new Vector2(innerWidth, rows.Length * rowHeight));
    }

    private static ListColumns Columns(float width, float scale)
    {
        var showObserved = width >= NarrowObserved * scale;
        var showSize = width >= NarrowSize * scale;
        var phase = PhaseColumn;
        var fixedWidth = (CheckColumn + PriceColumn + EntriesColumn + phase + (showSize ? SizeColumn : 0f) + (showObserved ? ObservedColumn : 0f)) * scale;
        var showEntries = width - fixedWidth >= MinimumAddressColumn * scale;
        if (!showEntries)
        {
            fixedWidth -= EntriesColumn * scale;
        }

        var phaseText = width - fixedWidth >= MinimumAddressColumn * scale;
        if (!phaseText)
        {
            fixedWidth -= (PhaseColumn - CheckColumn) * scale;
            phase = CheckColumn;
        }

        return new ListColumns(MathF.Max(MinimumAddressFallback * scale, width - fixedWidth), SizeColumn * scale, PriceColumn * scale, EntriesColumn * scale, phase * scale, ObservedColumn * scale, showSize, showObserved, showEntries, phaseText);
    }

    private static void DrawListHeader(in ListColumns columns, float width)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        using (Fonts.PushCaption())
        {
            var y = origin.Y;
            var x = origin.X + CheckColumn * scale;
            TextDraw.SmallCaps(Loc.T(L.Find.ColumnPlot), new Vector2(x, y), Styling.TextMuted);
            x += columns.Address;
            if (columns.ShowSize)
            {
                TextDraw.SmallCaps(Loc.T(L.Find.ColumnSize), new Vector2(x, y), Styling.TextMuted);
                x += columns.Size;
            }

            TextDraw.SmallCaps(Loc.T(L.Find.ColumnPrice), new Vector2(x, y), Styling.TextMuted);
            x += columns.Price;
            if (columns.ShowEntries)
            {
                TextDraw.SmallCaps(Loc.T(L.Find.ColumnEntries), new Vector2(x, y), Styling.TextMuted);
                x += columns.Entries;
            }

            if (columns.PhaseText)
            {
                TextDraw.SmallCaps(Loc.T(L.Find.ColumnPhase), new Vector2(x, y), Styling.TextMuted);
            }

            x += columns.Phase;
            if (columns.ShowObserved)
            {
                TextDraw.SmallCaps(Loc.T(L.Find.ColumnObserved), new Vector2(x, y), Styling.TextMuted);
            }

            var height = TextDraw.LineHeight() + 6f * scale;
            Paint.Hairline(ImGui.GetWindowDrawList(), origin + new Vector2(0f, height - 1f), origin + new Vector2(width, height - 1f));
            ImGui.Dummy(new Vector2(width, height));
        }
    }

    private void DrawRow(in ResultRow row, int index, in ListColumns columns, float width, float height, DateTime now)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var shortlisted = State.Shortlist.Contains(row.Key);
        ImGui.PushID(index);
        var checkHit = Hit.Area("##check", new Vector2(CheckColumn * scale, height));
        ImGui.SetCursorScreenPos(origin + new Vector2(CheckColumn * scale, 0f));
        var rowHit = Hit.Area("##row", new Vector2(MathF.Max(1f, width - CheckColumn * scale), height));
        ImGui.PopID();

        var selected = details == row.Key;
        if (selected || rowHit.Hovered || checkHit.Hovered)
        {
            Paint.Fill(drawList, origin, origin + new Vector2(width, height), Styling.WithAlpha(selected ? Styling.AccentGlow : Styling.Surface2, selected ? 0.14f : 0.6f), Styling.FrameRounding * scale);
        }

        var midY = origin.Y + height * 0.5f;
        DrawCheck(drawList, new Vector2(origin.X + CheckColumn * scale * 0.5f, midY), shortlisted, checkHit.Hovered);
        if (checkHit.Clicked)
        {
            State.Toggle(row.Key);
        }

        if (checkHit.Hovered)
        {
            Tooltip.Show(Loc.T(L.Find.ShortlistToggle));
        }

        if (rowHit.Clicked)
        {
            details = row.Key;
        }

        var text = rowTexts.TryGetValue(row.Key, out var cached) ? cached : CacheRowText(row, now);
        var textY = midY - TextDraw.LineHeight() * 0.5f;
        var x = origin.X + CheckColumn * scale;
        TextDraw.At(TextDraw.Truncate(PlotLabels.Address(Discovery.Names, row.Key), columns.Address - CellPad * scale), new Vector2(x, textY), Styling.TextStrong);
        x += columns.Address;
        if (columns.ShowSize)
        {
            TextDraw.At(text.Size, new Vector2(x, textY), Styling.TextSecondary);
            x += columns.Size;
        }

        TextDraw.At(text.Price, new Vector2(x, textY), row.Price is null ? Styling.TextMuted : Styling.TextSecondary);
        x += columns.Price;
        if (columns.ShowEntries)
        {
            TextDraw.At(text.Entries, new Vector2(x, textY), row.Entries is null ? Styling.TextMuted : Styling.TextSecondary);
            x += columns.Entries;
        }

        var markRadius = StatusMarkRadius * scale;
        StatusMarks.Draw(drawList, new Vector2(x + markRadius, midY), markRadius, row.Status);
        if (columns.PhaseText)
        {
            TextDraw.At(TextDraw.Truncate(text.Phase, columns.Phase - markRadius * 2f - CellPad * scale * 1.5f), new Vector2(x + markRadius * 2f + CellPad * scale * 0.75f, textY), StatusMarks.Color(row.Status));
        }

        x += columns.Phase;
        if (columns.ShowObserved)
        {
            TextDraw.At(text.Observed, new Vector2(x, textY), Styling.TextDim);
        }

        if (rowHit.Hovered)
        {
            ShowPlotTooltip(State.View, row.Key, now);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
    }

    private static void DrawCheck(ImDrawListPtr drawList, Vector2 center, bool on, bool hovered)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var half = CheckBox * scale * 0.5f;
        var min = center - new Vector2(half);
        var max = center + new Vector2(half);
        var rounding = 4f * scale;
        if (on)
        {
            Paint.Fill(drawList, min, max, Styling.AccentGlow, rounding);
            Paint.Check(drawList, center, half * 1.2f, Styling.InkOnGlow, 2f * scale);
            return;
        }

        Paint.Fill(drawList, min, max, Styling.WithAlpha(Styling.Surface1, 0.9f), rounding);
        Paint.Stroke(drawList, min, max, Styling.WithAlpha(hovered ? Styling.AccentGlowSoft : Styling.BorderDim, 0.9f), rounding, 1.4f * scale);
    }

    // Row texts depend on the row, the minute (for ages) and the language, so they are formatted once per change and
    // only for rows that have been on screen.
    private void RefreshRowTexts(DateTime now)
    {
        var minute = now.Ticks / TimeSpan.TicksPerMinute;
        if (ReferenceEquals(rowTextsFor.Results, shownResults) && rowTextsFor.Minute == minute && ReferenceEquals(rowTextsFor.Language, Loc.Current))
        {
            return;
        }

        rowTextsFor = (shownResults, minute, Loc.Current);
        rowTexts.Clear();
    }

    private RowText CacheRowText(in ResultRow row, DateTime now)
    {
        var text = new RowText(FindText.Size(row.Size), FindText.Gil(row.Price), FindText.Count(row.Entries), FindText.Status(row.Status), FindText.Age(row.ObservedUtc, now));
        rowTexts[row.Key] = text;
        return text;
    }
}
