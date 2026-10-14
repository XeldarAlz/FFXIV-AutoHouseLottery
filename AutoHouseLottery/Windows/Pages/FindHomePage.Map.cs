using AutoHouseLottery.Core.Find;
using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Core.Maps.Model;
using AutoHouseLottery.Windows.Components;
using AutoHouseLottery.Windows.Find;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using ECommons.DalamudServices;
using System.Numerics;

namespace AutoHouseLottery.Windows.Pages;

internal sealed partial class FindHomePage
{
    private const float MarkerRadius = 9f;
    private const float DimmedMarkerScale = 0.7f;
    private const float DimmedAlpha = 0.45f;
    private const float HoverReach = 4f;
    private const float ClickSlop = 4f;
    private const float MinimumZoom = 1f;
    private const float MaximumZoom = 4f;
    private const float ZoomStep = 0.15f;
    private const float PointOfInterestSize = 20f;
    private const float LegendMarkRadius = 5f;
    private const float LegendGap = 14f;
    private const float DistrictPickerWidth = 190f;
    private const float WardPickerWidth = 150f;
    private const float DivisionSwitchWidth = 220f;
    private const string CanvasId = "##ahl_find_map_canvas";

    private static readonly PlotStatus[] legendStatuses =
        [PlotStatus.EntryOpen, PlotStatus.Results, PlotStatus.NextCycle, PlotStatus.ForSale, PlotStatus.NeedsRefresh, PlotStatus.NotReported, PlotStatus.Occupied];

    private readonly ushort[] mapDistrictIds = new ushort[HousingDistricts.All.Length];
    private readonly string[] mapDistrictLabels = new string[HousingDistricts.All.Length];
    private string[] mapWardLabels = [];
    private int mapDistrictCount;
    private ushort mapDistrict;
    private int mapWard = 1;
    private bool mapSubdivision;
    private float mapZoom = MinimumZoom;
    private Vector2 mapPan;
    private Vector2 pressPosition;
    private (ushort District, int Query) wardPickedFor;
    private (FindResults? Results, ushort District, LanguageInfo? Language, bool Named) wardLabelsFor;
    private (uint Mask, LanguageInfo? Language, bool Named) districtLabelsFor;
    private (PlotKey Key, int View, long Minute, LanguageInfo? Language, bool Named) tooltipFor;
    private PlotTooltipText tooltipText;

    private void DrawMap(FindView view, DateTime now)
    {
        var area = State.Area;
        RefreshMapDistricts(area);
        if (mapDistrictCount == 0)
        {
            Message(FontAwesomeIcon.MapMarkedAlt, Loc.T(L.Find.NoDistrictChosen));
            return;
        }

        PickWard();
        DrawMapBar();
        Styling.VSpace(6f);

        var scale = ImGuiHelpers.GlobalScale;
        var legendHeight = TextDraw.CaptionLineHeight() + 8f * scale;
        var available = ImGui.GetContentRegionAvail();
        var canvas = new Vector2(available.X, MathF.Max(1f, available.Y - legendHeight));
        var map = Discovery.Maps.Get(mapDistrict);
        if (map is null)
        {
            Message(FontAwesomeIcon.Hourglass, Loc.T(L.Find.MapLoading));
            return;
        }

        if (map.For(mapSubdivision) is not { } division)
        {
            Message(FontAwesomeIcon.MapMarkedAlt, Loc.T(L.Find.MapUnavailable));
            Styling.TextCentered(Loc.T(L.Find.MapUnavailableHint), Styling.TextDim);
            return;
        }

        DrawCanvas(view, division, canvas, now);
        DrawLegend();
    }

    private void RefreshMapDistricts(in AreaScope area)
    {
        var named = Discovery.Names.Ready;
        if (districtLabelsFor != (area.DistrictMask, Loc.Current, named))
        {
            districtLabelsFor = (area.DistrictMask, Loc.Current, named);
            mapDistrictCount = 0;
            for (var index = 0; index < HousingDistricts.All.Length; index++)
            {
                var districtId = HousingDistricts.All[index].TerritoryId;
                if (!area.IncludesDistrict(districtId))
                {
                    continue;
                }

                mapDistrictIds[mapDistrictCount] = districtId;
                mapDistrictLabels[mapDistrictCount] = Discovery.Names.District(districtId);
                mapDistrictCount++;
            }
        }

        if (mapDistrictCount > 0 && Array.IndexOf(mapDistrictIds, mapDistrict, 0, mapDistrictCount) < 0)
        {
            mapDistrict = mapDistrictIds[0];
        }
    }

    // Opens on the ward with the most matches when the district's first matches arrive and the chosen ward has none.
    private void PickWard()
    {
        if (wardPickedFor == (mapDistrict, shownQueryRevision) || shownResults.Count == 0)
        {
            return;
        }

        wardPickedFor = (mapDistrict, shownQueryRevision);
        if (shownResults.MatchesInWard(mapDistrict, mapWard) > 0)
        {
            return;
        }

        var best = 0;
        HousingDistricts.TryGet(mapDistrict, out var district);
        for (var ward = 1; ward <= district.WardCount; ward++)
        {
            var matches = shownResults.MatchesInWard(mapDistrict, ward);
            if (matches > best)
            {
                best = matches;
                mapWard = ward;
            }
        }
    }

    // The three pickers shrink together when the results panel is narrower than they are, so the division switch stays
    // reachable at the smallest window size.
    private void DrawMapBar()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var natural = (DistrictPickerWidth + WardPickerWidth + DivisionSwitchWidth) * scale + spacing * 2f;
        var fit = MathF.Min(1f, ImGui.GetContentRegionAvail().X / natural);
        var selected = Array.IndexOf(mapDistrictIds, mapDistrict, 0, mapDistrictCount);
        if (Dropdown.Draw("##ahl_find_map_district", mapDistrictLabels.AsSpan(0, mapDistrictCount), ref selected, DistrictPickerWidth * fit, DistrictPickerWidth))
        {
            mapDistrict = mapDistrictIds[selected];
            mapPan = Vector2.Zero;
            mapZoom = MinimumZoom;
        }

        ImGui.SameLine();
        RefreshWardLabels();
        var ward = mapWard - 1;
        if (Dropdown.Draw("##ahl_find_map_ward", mapWardLabels, ref ward, WardPickerWidth * fit, WardPickerWidth, searchable: false))
        {
            mapWard = ward + 1;
        }

        ImGui.SameLine();
        var divisions = (ReadOnlySpan<Segmented.Item>)[new(null, Loc.T(L.Find.DivisionMain)), new(null, Loc.T(L.Find.DivisionSubdivision))];
        var division = mapSubdivision ? 1 : 0;
        var origin = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(origin - new Vector2(0f, (Layout.ChipHeight * scale - ImGui.GetFrameHeight()) * 0.5f));
        if (Segmented.Draw("##ahl_find_map_division", divisions, ref division, height: Layout.ChipHeight, width: DivisionSwitchWidth * scale * fit))
        {
            mapSubdivision = division == 1;
        }
    }

    private void RefreshWardLabels()
    {
        var named = Discovery.Names.Ready;
        if (ReferenceEquals(wardLabelsFor.Results, shownResults) && wardLabelsFor.District == mapDistrict && ReferenceEquals(wardLabelsFor.Language, Loc.Current) && wardLabelsFor.Named == named)
        {
            return;
        }

        wardLabelsFor = (shownResults, mapDistrict, Loc.Current, named);
        HousingDistricts.TryGet(mapDistrict, out var district);
        if (mapWardLabels.Length != district.WardCount)
        {
            mapWardLabels = new string[district.WardCount];
        }

        for (var ward = 1; ward <= district.WardCount; ward++)
        {
            var matches = shownResults.MatchesInWard(mapDistrict, ward);
            mapWardLabels[ward - 1] = matches > 0 ? Loc.T(L.Find.WardMatches, ward, matches) : Loc.T(L.Find.Ward, ward);
        }

        mapWard = Math.Clamp(mapWard, 1, Math.Max(1, (int)district.WardCount));
    }

    private void DrawCanvas(FindView view, DivisionMap division, Vector2 size, DateTime now)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var clicked = ImGui.InvisibleButton(CanvasId, size);
        var hovered = ImGui.IsItemHovered();
        var io = ImGui.GetIO();
        if (ImGui.IsItemActivated())
        {
            pressPosition = io.MousePos;
        }

        var side = MathF.Min(size.X, size.Y) * mapZoom;
        if (hovered && io.MouseWheel != 0f)
        {
            var zoom = Math.Clamp(mapZoom * (1f + io.MouseWheel * ZoomStep), MinimumZoom, MaximumZoom);
            var fromCenter = io.MousePos - (origin + size * 0.5f + mapPan);
            mapPan += fromCenter - fromCenter * (zoom / mapZoom);
            mapZoom = zoom;
            side = MathF.Min(size.X, size.Y) * mapZoom;
        }

        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, ClickSlop * scale))
        {
            mapPan += io.MouseDelta;
        }

        var limit = side * 0.5f;
        mapPan = Vector2.Clamp(mapPan, new Vector2(-limit), new Vector2(limit));
        var imageMin = origin + size * 0.5f + mapPan - new Vector2(side * 0.5f);
        drawList.PushClipRect(origin, origin + size, true);
        if (Svc.Texture.GetFromGame(division.TexturePath).GetWrapOrDefault() is { } texture)
        {
            drawList.AddImage(texture.Handle, imageMin, imageMin + new Vector2(side));
        }

        DrawPointsOfInterest(drawList, division, imageMin, side);
        var hoveredKey = DrawMarkers(view, division, imageMin, side, hovered ? io.MousePos : new Vector2(float.MinValue));
        drawList.PopClipRect();

        if (hoveredKey is { } target)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (!ImGui.IsMouseDragging(ImGuiMouseButton.Left, ClickSlop * scale))
            {
                ShowPlotTooltip(view, target, now);
            }

            if (clicked && Vector2.Distance(pressPosition, io.MousePos) <= ClickSlop * scale)
            {
                details = target;
            }
        }

        using (Fonts.PushCaption())
        {
            var hint = Loc.T(L.Find.MapHint);
            TextDraw.At(hint, origin + new Vector2(8f * scale, size.Y - TextDraw.LineHeight() - 6f * scale), Styling.WithAlpha(Styling.TextDim, 0.85f));
        }
    }

    private static void DrawPointsOfInterest(ImDrawListPtr drawList, DivisionMap division, Vector2 imageMin, float side)
    {
        var half = PointOfInterestSize * ImGuiHelpers.GlobalScale * 0.5f;
        for (var index = 0; index < division.PointsOfInterest.Length; index++)
        {
            var point = division.PointsOfInterest[index];
            var center = imageMin + point.Position * side;
            var icon = Svc.Texture.GetFromGameIcon(new GameIconLookup(point.IconId)).GetWrapOrEmpty();
            drawList.AddImage(icon.Handle, center - new Vector2(half), center + new Vector2(half));
        }
    }

    private PlotKey? DrawMarkers(FindView view, DivisionMap division, Vector2 imageMin, float side, Vector2 mouse)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        var world = Discovery.World;
        PlotKey? hoveredKey = null;
        var closest = float.MaxValue;
        using var caption = Fonts.PushCaption();
        for (var index = 0; index < division.Plots.Length; index++)
        {
            var point = division.Plots[index];
            if (!PlotKey.TryCreate(GameRegion.Global, world, mapDistrict, mapWard, point.Plot, out var key))
            {
                continue;
            }

            var status = view.Plots.TryGetValue(key, out var plot) ? PlotStatuses.Classify(plot, view.Position) : (PlotStatus?)null;
            var matching = shownResults.Matching.Contains(key);
            var center = imageMin + point.Position * side;
            var radius = MarkerRadius * scale * (matching ? 1f : DimmedMarkerScale);
            var alpha = matching ? 1f : DimmedAlpha;
            StatusMarks.Draw(drawList, center, radius, status, alpha);
            if (State.Shortlist.Contains(key))
            {
                StatusMarks.Shortlisted(drawList, center, radius, alpha);
            }

            if (Discovery.Saved.Library.IsSaved(key))
            {
                TextDraw.IconCentered(FontAwesomeIcon.Star, center + new Vector2(radius, -radius), Styling.WithAlpha(Styling.AccentAmber, alpha));
            }

            if (matching)
            {
                var number = wardNumbers[point.Plot - 1];
                TextDraw.Middle(number, center - new Vector2(radius), center + new Vector2(radius), Styling.ForegroundOn(StatusMarks.Color(status)));
            }

            var distance = Vector2.Distance(mouse, center);
            if (distance <= radius + HoverReach * scale && distance < closest)
            {
                closest = distance;
                hoveredKey = key;
            }
        }

        return hoveredKey;
    }

    private void ShowPlotTooltip(FindView view, PlotKey key, DateTime now)
    {
        RefreshPlotTooltip(view, key, now);
        using (Tooltip.Begin())
        {
            Tooltip.Text(tooltipText.Address, Styling.TextStrong);
            if (tooltipText.Row is not { } row)
            {
                Tooltip.Text(tooltipText.Phase);
                return;
            }

            Tooltip.Text(tooltipText.Phase, StatusMarks.Color(row.Status));
            Tooltip.Text(tooltipText.Deposit);
            Tooltip.Text(tooltipText.Entries);
            Tooltip.Text(tooltipText.Observed, Styling.TextDim);
        }
    }

    // A tooltip stays up for many frames under a resting pointer, so its lines are formatted once per plot, view, minute
    // and language.
    private void RefreshPlotTooltip(FindView view, PlotKey key, DateTime now)
    {
        var inputs = (key, view.Revision, now.Ticks / TimeSpan.TicksPerMinute, Loc.Current, Discovery.Names.Ready);
        if (inputs == tooltipFor)
        {
            return;
        }

        tooltipFor = inputs;
        var address = PlotLabels.Address(Discovery.Names, key);
        if (view.Plot(key) is not { } plot)
        {
            tooltipText = new PlotTooltipText(address, null, Loc.T(L.Find.NoListing), string.Empty, string.Empty, string.Empty);
            return;
        }

        var row = PlotStatuses.Row(plot, view.Position);
        tooltipText = new PlotTooltipText(
            address,
            row,
            string.Concat(FindText.Status(row.Status), TextDraw.Separator, FindText.Size(row.Size)),
            Loc.T(L.Find.Deposit, FindText.Gil(row.Price)),
            Loc.T(L.Find.EntriesValue, FindText.Count(row.Entries)),
            FindText.Observed(row.ObservedUtc, now));
    }

    private readonly record struct PlotTooltipText(string Address, ResultRow? Row, string Phase, string Deposit, string Entries, string Observed);

    private void DrawLegend()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos() + new Vector2(0f, 6f * scale);
        var right = origin.X + ImGui.GetContentRegionAvail().X;
        var drawList = ImGui.GetWindowDrawList();
        var radius = LegendMarkRadius * scale;
        using var caption = Fonts.PushCaption();
        var midY = origin.Y + TextDraw.LineHeight() * 0.5f;
        var x = origin.X + radius;
        for (var index = 0; index < legendStatuses.Length + 2; index++)
        {
            var label = index < legendStatuses.Length ? FindText.Status(legendStatuses[index])
                : index == legendStatuses.Length ? Loc.T(L.Find.LegendShortlisted) : Loc.T(L.Find.LegendSaved);
            var width = radius * 2f + 6f * scale + TextDraw.Measure(label).X;
            if (x + width > right)
            {
                break;
            }

            var center = new Vector2(x, midY);
            if (index < legendStatuses.Length)
            {
                StatusMarks.Draw(drawList, center, radius, legendStatuses[index]);
            }
            else if (index == legendStatuses.Length)
            {
                StatusMarks.Shortlisted(drawList, center, radius * 0.6f);
            }
            else
            {
                TextDraw.IconCentered(FontAwesomeIcon.Star, center, Styling.AccentAmber);
            }

            TextDraw.At(label, new Vector2(x + radius + 6f * scale, midY - TextDraw.LineHeight() * 0.5f), Styling.TextDim);
            x += width + LegendGap * scale;
        }

        ImGui.Dummy(new Vector2(right - origin.X, TextDraw.LineHeight() + 6f * scale));
    }
}
