using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Windows.Components;
using AutoHouseLottery.Windows.Find;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Globalization;
using System.Numerics;

namespace AutoHouseLottery.Windows.Pages;

internal sealed partial class FindHomePage
{
    private const int WardColumns = 6;
    private const int RangeDigits = 2;
    private const float RangeFieldWidth = 52f;
    private const float StarSize = 22f;
    private const float AgeDropdownWidth = 150f;

    private static readonly int[] ageChoices = [0, 1, 6, 24, 72];
    private static readonly PlotStatus[] extraStatuses = [PlotStatus.ForSale, PlotStatus.NotReported, PlotStatus.NeedsRefresh, PlotStatus.Occupied];
    private static readonly string[] wardNumbers = WardNumbers();
    private static readonly string[] wardLabels = WardLabels();

    private readonly string[] districtLabels = new string[HousingDistricts.All.Length];
    private readonly string[] ageLabels = new string[ageChoices.Length];
    private LanguageInfo? districtLabelsLanguage;
    private LanguageInfo? ageLabelsLanguage;
    private bool districtLabelsNamed;
    private string rangeFrom = string.Empty;
    private string rangeTo = string.Empty;
    private string plotFrom = string.Empty;
    private string plotTo = string.Empty;
    private string entriesFrom = string.Empty;
    private string entriesTo = string.Empty;
    private PlotFilter fieldsShown;

    private void DrawAreasPopup()
    {
        ImGui.SetNextWindowPos(areasAnchor, ImGuiCond.Appearing);
        using var popup = ContextMenu.Begin(AreasPopup);
        if (!popup.Open)
        {
            return;
        }

        RefreshDistrictLabels();
        var area = State.Area;
        var original = area;
        Styling.SectionLabel(Loc.T(L.Find.AreasTitle));
        var scale = ImGuiHelpers.GlobalScale;
        for (var index = 0; index < HousingDistricts.All.Length; index++)
        {
            var districtId = HousingDistricts.All[index].TerritoryId;
            var preferred = State.IsPreferred(districtId);
            ImGui.PushID(index);
            if (IconButton.Draw(FontAwesomeIcon.Heart, "##ahl_find_prefer", StarSize * scale, preferred ? Styling.AccentRose : Styling.TextMuted, Loc.T(L.Find.PreferDistrict)))
            {
                State.TogglePreferred(districtId);
            }

            ImGui.SameLine();
            var included = area.IncludesDistrict(districtId);
            if (ImGui.Checkbox(districtLabels[index], ref included))
            {
                area = area with { DistrictMask = area.DistrictMask ^ AreaScope.DistrictBit(districtId) };
            }

            ImGui.PopID();
        }

        if (PillButton.Draw("##ahl_find_areas_all", Loc.T(L.Find.SelectAll), Styling.AccentGlow, PillButton.Emphasis.Ghost))
        {
            area = area with { DistrictMask = AreaScope.AllDistrictsMask };
        }

        ImGui.SameLine();
        if (PillButton.Draw("##ahl_find_areas_none", Loc.T(L.Find.ClearSelection), Styling.AccentGlow, PillButton.Emphasis.Ghost))
        {
            area = area with { DistrictMask = 0 };
        }

        ImGui.Separator();
        area = DrawWardChoice(area);
        ImGui.Separator();
        TextDraw.Hint(Loc.T(L.Find.Divisions));
        var main = area.MainDivision;
        if (ImGui.Checkbox(Loc.T(L.Find.DivisionMain), ref main))
        {
            area = area with { MainDivision = main };
        }

        ImGui.SameLine();
        var subdivision = area.Subdivision;
        if (ImGui.Checkbox(Loc.T(L.Find.DivisionSubdivision), ref subdivision))
        {
            area = area with { Subdivision = subdivision };
        }

        if (area != original)
        {
            State.SetArea(area);
        }
    }

    private AreaScope DrawWardChoice(AreaScope area)
    {
        var wardCount = MostWards(area);
        var all = area.AllWards;
        if (ImGui.Checkbox(Loc.T(L.Find.AllWards), ref all))
        {
            area = all ? area with { AllWards = true, WardMask = 0 } : area with { AllWards = false, WardMask = AreaScope.WardRange(1, wardCount) };
        }

        if (!area.AllWards)
        {
            for (var ward = 1; ward <= wardCount; ward++)
            {
                if ((ward - 1) % WardColumns != 0)
                {
                    ImGui.SameLine();
                }

                var included = area.IncludesWard(ward);
                if (ImGui.Checkbox(wardLabels[ward - 1], ref included))
                {
                    area = area with { WardMask = area.WardMask ^ AreaScope.WardBit(ward) };
                }
            }
        }

        TextDraw.Hint(Loc.T(L.Find.WardRange));
        NumberField.Draw("##ahl_find_range_from", ref rangeFrom, "1", RangeFieldWidth, RangeDigits);
        ImGui.SameLine();
        ImGui.TextUnformatted(Loc.T(L.Find.WardRangeTo));
        ImGui.SameLine();
        NumberField.Draw("##ahl_find_range_to", ref rangeTo, wardNumbers[wardCount - 1], RangeFieldWidth, RangeDigits);
        ImGui.SameLine();
        var hasRange = TryWard(rangeFrom, 1, wardCount, out var first) & TryWard(rangeTo, wardCount, wardCount, out var last) && first <= last;
        if (PillButton.Draw("##ahl_find_range_apply", Loc.T(L.Find.ApplyRange), Styling.AccentGlow, PillButton.Emphasis.Tinted, enabled: hasRange))
        {
            area = area with { AllWards = false, WardMask = AreaScope.WardRange(first, last) };
        }

        return area;
    }

    private static bool TryWard(string text, int fallback, int wardCount, out int ward)
    {
        if (text.Length == 0)
        {
            ward = fallback;
            return true;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ward) && ward >= 1 && ward <= wardCount;
    }

    private static int MostWards(in AreaScope area)
    {
        var wards = 1;
        for (var index = 0; index < HousingDistricts.All.Length; index++)
        {
            var district = HousingDistricts.All[index];
            if ((area.DistrictMask == 0 || area.IncludesDistrict(district.TerritoryId)) && district.WardCount > wards)
            {
                wards = district.WardCount;
            }
        }

        return wards;
    }

    private void DrawMorePopup()
    {
        ImGui.SetNextWindowPos(moreAnchor, ImGuiCond.Appearing);
        using var popup = ContextMenu.Begin(MorePopup);
        if (!popup.Open)
        {
            return;
        }

        var filter = State.Filter;
        if (filter != fieldsShown)
        {
            fieldsShown = filter;
            plotFrom = filter.FirstPlot == PlotFilter.FirstPlotNumber ? string.Empty : filter.FirstPlot.ToString(CultureInfo.InvariantCulture);
            plotTo = filter.LastPlot == PlotFilter.LastPlotNumber ? string.Empty : filter.LastPlot.ToString(CultureInfo.InvariantCulture);
            entriesFrom = filter.MinimumEntries?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            entriesTo = filter.MaximumEntries?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        }

        var updated = filter;
        Styling.SectionLabel(Loc.T(L.Find.MoreFilters));
        TextDraw.Hint(Loc.T(L.Find.PlotNumbers));
        var plotsChanged = NumberField.Draw("##ahl_find_plot_from", ref plotFrom, "1", RangeFieldWidth, RangeDigits);
        ImGui.SameLine();
        ImGui.TextUnformatted(Loc.T(L.Find.WardRangeTo));
        ImGui.SameLine();
        plotsChanged |= NumberField.Draw("##ahl_find_plot_to", ref plotTo, "60", RangeFieldWidth, RangeDigits);
        if (plotsChanged)
        {
            updated = updated with
            {
                FirstPlot = ParseBounded(plotFrom, PlotFilter.FirstPlotNumber),
                LastPlot = ParseBounded(plotTo, PlotFilter.LastPlotNumber),
            };
        }

        TextDraw.Hint(Loc.T(L.Find.ReportedEntries));
        ImGui.TextUnformatted(Loc.T(L.Find.AtLeast));
        ImGui.SameLine();
        var entriesChanged = NumberField.Draw("##ahl_find_entries_from", ref entriesFrom, string.Empty, RangeFieldWidth, RangeDigits + 1);
        ImGui.SameLine();
        ImGui.TextUnformatted(Loc.T(L.Find.AtMost));
        ImGui.SameLine();
        entriesChanged |= NumberField.Draw("##ahl_find_entries_to", ref entriesTo, string.Empty, RangeFieldWidth, RangeDigits + 1);
        if (entriesChanged)
        {
            updated = updated with { MinimumEntries = ParseOptional(entriesFrom), MaximumEntries = ParseOptional(entriesTo) };
        }

        TextDraw.Hint(Loc.T(L.Find.ObservedWithin));
        if (!ReferenceEquals(ageLabelsLanguage, Loc.Current))
        {
            ageLabelsLanguage = Loc.Current;
            for (var index = 0; index < ageChoices.Length; index++)
            {
                ageLabels[index] = ageChoices[index] == 0 ? Loc.T(L.Find.AnyTime) : FindText.Hours(ageChoices[index]);
            }
        }

        var age = Math.Max(0, Array.IndexOf(ageChoices, filter.MaximumAgeHours));
        if (Dropdown.Draw("##ahl_find_age", ageLabels, ref age, AgeDropdownWidth))
        {
            updated = updated with { MaximumAgeHours = ageChoices[age] };
        }

        TextDraw.Hint(Loc.T(L.Find.AlsoShow));
        for (var index = 0; index < extraStatuses.Length; index++)
        {
            var bit = PlotFilter.ToFilter(extraStatuses[index]);
            var shown = (updated.Statuses & bit) != 0;
            ImGui.PushID(index);
            if (ImGui.Checkbox(FindText.Status(extraStatuses[index]), ref shown))
            {
                updated = updated with { Statuses = updated.Statuses ^ bit };
            }

            if (ImGui.IsItemHovered())
            {
                Tooltip.Show(FindText.StatusHelp(extraStatuses[index]));
            }

            ImGui.PopID();
        }

        if (PillButton.Draw("##ahl_find_preset_small", Loc.T(L.Find.PresetSmall), Styling.AccentMint, PillButton.Emphasis.Tinted, FontAwesomeIcon.Home, tooltip: Loc.T(L.Find.PresetSmallHelp)))
        {
            updated = updated with { Sizes = SizeFilter.Small, Statuses = StatusFilter.EntryOpen | StatusFilter.Results | StatusFilter.NextCycle };
        }

        ImGui.SameLine();
        if (PillButton.Draw("##ahl_find_reset", Loc.T(L.Find.ResetFilters), Styling.AccentGlow, PillButton.Emphasis.Ghost, FontAwesomeIcon.Undo))
        {
            updated = PlotFilter.Default with { Buyer = filter.Buyer, PriceCeiling = filter.PriceCeiling };
        }

        if (updated != filter)
        {
            fieldsShown = updated;
            State.SetFilter(updated);
        }
    }

    private void RefreshDistrictLabels()
    {
        if (ReferenceEquals(districtLabelsLanguage, Loc.Current) && districtLabelsNamed == Discovery.Names.Ready && districtLabels[0] is not null)
        {
            return;
        }

        districtLabelsLanguage = Loc.Current;
        districtLabelsNamed = Discovery.Names.Ready;
        for (var index = 0; index < districtLabels.Length; index++)
        {
            districtLabels[index] = string.Concat(Discovery.Names.District(HousingDistricts.All[index].TerritoryId), "##ahl_district");
        }
    }

    private static int ParseBounded(string text, int fallback)
    {
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, PlotFilter.FirstPlotNumber, PlotFilter.LastPlotNumber)
            : fallback;
    }

    private static int? ParseOptional(string text) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static string[] WardNumbers()
    {
        var numbers = new string[AreaScope.MaximumWards];
        for (var index = 0; index < numbers.Length; index++)
        {
            numbers[index] = (index + 1).ToString(CultureInfo.InvariantCulture);
        }

        return numbers;
    }

    private static string[] WardLabels()
    {
        var labels = new string[AreaScope.MaximumWards];
        for (var index = 0; index < labels.Length; index++)
        {
            labels[index] = string.Concat(wardNumbers[index], "##ahl_ward_", wardNumbers[index]);
        }

        return labels;
    }
}
