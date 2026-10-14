using AutoHouseLottery.Core.Find;
using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Core.Providers.Model;
using AutoHouseLottery.Windows.Components;
using AutoHouseLottery.Windows.Find;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Collections.Immutable;
using System.Numerics;

namespace AutoHouseLottery.Windows.Pages;

internal sealed partial class FindHomePage
{
    private const float WorldPickerWidth = 180f;
    private const float WorldPanelWidth = 240f;
    private const float PriceFieldWidth = 120f;
    private const float SortWidth = 170f;
    private const float ViewSwitchWidth = 170f;
    private const float ItemGap = 8f;
    private const float GroupGap = 18f;
    private const string AreasPopup = "##ahl_find_areas";
    private const string MorePopup = "##ahl_find_more";

    private static readonly ResultSort[] sortOrder = [ResultSort.PreferredAreas, ResultSort.FewestEntries, ResultSort.LowestDeposit, ResultSort.RecentlyObserved, ResultSort.Address];
    private static readonly PlotStatus[] chipStatuses = [PlotStatus.EntryOpen, PlotStatus.Results, PlotStatus.NextCycle];
    private static readonly string[] chipIds = ["##ahl_find_status_open", "##ahl_find_status_results", "##ahl_find_status_next"];

    private readonly string[] sortLabels = new string[5];
    private readonly GilField.Buffer priceBuffer = new();
    private ImmutableArray<FeedWorld> labelledWorlds;
    private uint labelledHome = uint.MaxValue;
    private LanguageInfo? worldLabelsLanguage;
    private uint[] worldOptions = [];
    private string[] worldLabels = [];
    private CachedText refreshText;
    private CachedText ageText;
    private CachedText moreText;
    private Vector2 areasAnchor;
    private Vector2 moreAnchor;

    private void DrawContextRow()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = Layout.ChipHeight * scale;
        using (Fonts.PushHeadline())
        {
            var title = Loc.T(L.Find.Title);
            TextDraw.At(title, new Vector2(origin.X, origin.Y + (height - TextDraw.Measure(title).Y) * 0.5f), Styling.TextStrong);
        }

        var gap = ItemGap * scale;
        var right = origin.X + width;
        var filter = State.Filter;
        var freeCompanyLabel = Loc.T(L.Find.BuyerFreeCompany);
        var privateLabel = Loc.T(L.Find.BuyerPrivate);
        var tooltip = Loc.T(L.Find.BuyerTooltip);
        right -= FilterChip.Width(freeCompanyLabel, null);
        ImGui.SetCursorScreenPos(new Vector2(right, origin.Y));
        if (FilterChip.Draw("##ahl_find_buyer_fc", freeCompanyLabel, null, Styling.AccentNebula, filter.Buyer == BuyerType.FreeCompany, height, tooltip: tooltip))
        {
            State.SetFilter(filter with { Buyer = filter.Buyer == BuyerType.FreeCompany ? BuyerType.Unknown : BuyerType.FreeCompany });
        }

        right -= gap + FilterChip.Width(privateLabel, null);
        ImGui.SetCursorScreenPos(new Vector2(right, origin.Y));
        if (FilterChip.Draw("##ahl_find_buyer_private", privateLabel, null, Styling.AccentNebula, filter.Buyer == BuyerType.Private, height, tooltip: tooltip))
        {
            State.SetFilter(filter with { Buyer = filter.Buyer == BuyerType.Private ? BuyerType.Unknown : BuyerType.Private });
        }

        right -= GroupGap * scale + WorldPickerWidth * scale;
        ImGui.SetCursorScreenPos(new Vector2(right, origin.Y + (height - ImGui.GetFrameHeight()) * 0.5f));
        DrawWorldPicker();

        var character = Discovery.Character;
        var name = character is { } known ? known.Name : Loc.T(L.Find.NotLoggedIn);
        var iconSize = TextDraw.IconSize(FontAwesomeIcon.User);
        var nameWidth = TextDraw.Measure(name).X;
        var nameX = right - GroupGap * scale - nameWidth;
        var minimumX = origin.X + width * 0.25f;
        if (nameX - iconSize.X - gap > minimumX)
        {
            var midY = origin.Y + height * 0.5f;
            TextDraw.Icon(FontAwesomeIcon.User, new Vector2(nameX - iconSize.X - gap, midY - iconSize.Y * 0.5f), Styling.TextDim);
            TextDraw.At(name, new Vector2(nameX, midY - TextDraw.LineHeight() * 0.5f), character is null ? Styling.TextMuted : Styling.TextSecondary);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
    }

    private void DrawWorldPicker()
    {
        var home = Discovery.Character?.HomeWorldId ?? 0;
        var worlds = Discovery.Feed.Snapshot.WorldList;
        if (worlds != labelledWorlds || home != labelledHome || !ReferenceEquals(worldLabelsLanguage, Loc.Current) || worldLabels.Length == 0)
        {
            BuildWorldLabels(worlds, home);
        }

        var chosen = State.ChosenWorld;
        var selected = 0;
        for (var index = 1; index < worldOptions.Length; index++)
        {
            if (worldOptions[index] == chosen)
            {
                selected = index;
                break;
            }
        }

        if (Dropdown.Draw("##ahl_find_world", worldLabels, ref selected, WorldPickerWidth, WorldPanelWidth, searchable: true))
        {
            State.SetWorld(worldOptions[selected]);
        }

        if (ImGui.IsItemHovered())
        {
            Tooltip.Show(Loc.T(L.Find.WorldTooltip));
        }
    }

    private void BuildWorldLabels(ImmutableArray<FeedWorld> worlds, uint home)
    {
        labelledWorlds = worlds;
        labelledHome = home;
        worldLabelsLanguage = Loc.Current;
        var sorted = worlds.IsDefault ? [] : worlds.Sort(static (left, right) =>
        {
            var center = string.Compare(left.DataCenter, right.DataCenter, StringComparison.OrdinalIgnoreCase);
            return center != 0 ? center : string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        });
        worldOptions = new uint[sorted.Length + 1];
        worldLabels = new string[sorted.Length + 1];
        worldLabels[0] = home == 0 ? Loc.T(L.Find.NotLoggedIn) : Loc.T(L.Find.HomeWorld, Discovery.Names.World(home));
        for (var index = 0; index < sorted.Length; index++)
        {
            worldOptions[index + 1] = sorted[index].Id;
            worldLabels[index + 1] = string.Concat(sorted[index].Name, TextDraw.Separator, sorted[index].DataCenter);
        }
    }

    private void DrawFilterRow()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var height = Layout.ChipHeight * scale;
        var origin = ImGui.GetCursorScreenPos();
        var maximumX = origin.X + ImGui.GetContentRegionAvail().X;
        var position = origin;
        var filter = State.Filter;
        var gap = ItemGap * scale;

        var areasText = ScopeLabels(State.Area with { WorldId = 0 }).Areas;
        Place(ref position, origin.X, maximumX, height, FilterChip.Width(areasText, null, FontAwesomeIcon.ChevronDown), gap);
        if (FilterChip.Draw("##ahl_find_areas_chip", areasText, null, Styling.AccentGlow, !State.Area.SelectsNothing, height, FontAwesomeIcon.ChevronDown))
        {
            areasAnchor = position + new Vector2(0f, height + 4f * scale);
            ImGui.OpenPopup(AreasPopup);
        }

        Advance(ref position, FilterChip.Width(areasText, null, FontAwesomeIcon.ChevronDown), GroupGap * scale);
        DrawSizeChip(ref position, origin.X, maximumX, height, gap, filter, SizeFilter.Small, L.Find.SizeSmall, "##ahl_find_size_small");
        DrawSizeChip(ref position, origin.X, maximumX, height, gap, filter, SizeFilter.Medium, L.Find.SizeMedium, "##ahl_find_size_medium");
        DrawSizeChip(ref position, origin.X, maximumX, height, gap, filter, SizeFilter.Large, L.Find.SizeLarge, "##ahl_find_size_large");
        position.X += GroupGap * scale - gap;

        var upTo = Loc.T(L.Find.UpTo);
        var priceWidth = TextDraw.Measure(upTo).X + gap + PriceFieldWidth * scale;
        Place(ref position, origin.X, maximumX, height, priceWidth, gap);
        TextDraw.At(upTo, new Vector2(position.X, position.Y + (height - TextDraw.LineHeight()) * 0.5f), Styling.TextDim);
        ImGui.SetCursorScreenPos(new Vector2(position.X + TextDraw.Measure(upTo).X + gap, position.Y + (height - ImGui.GetFrameHeight()) * 0.5f));
        if (GilField.Draw("##ahl_find_price", priceBuffer, filter.PriceCeiling, long.MaxValue, Loc.T(L.Find.AnyPrice), PriceFieldWidth, out var ceiling, Loc.T(L.Find.PriceTooltip)))
        {
            State.SetFilter(filter with { PriceCeiling = ceiling });
        }

        Advance(ref position, priceWidth, GroupGap * scale);
        for (var index = 0; index < chipStatuses.Length; index++)
        {
            DrawStatusChip(ref position, origin.X, maximumX, height, gap, chipStatuses[index], chipIds[index]);
        }

        var more = filter.MoreFiltersActive;
        var moreLabel = more == 0 ? Loc.T(L.Find.MoreFilters) : moreText.Get(more, static count => Loc.T(L.Find.MoreFiltersCount, count));
        position.X += GroupGap * scale - gap;
        Place(ref position, origin.X, maximumX, height, FilterChip.Width(moreLabel, null, FontAwesomeIcon.SlidersH), gap);
        if (FilterChip.Draw("##ahl_find_more_chip", moreLabel, null, Styling.AccentBlue, more > 0, height, FontAwesomeIcon.SlidersH))
        {
            moreAnchor = position + new Vector2(0f, height + 4f * scale);
            ImGui.OpenPopup(MorePopup);
        }

        Advance(ref position, FilterChip.Width(moreLabel, null, FontAwesomeIcon.SlidersH), gap);
        DrawAreasPopup();
        DrawMorePopup();
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(maximumX - origin.X, position.Y + height - origin.Y));
    }

    private void DrawSizeChip(ref Vector2 position, float left, float maximumX, float height, float gap, in PlotFilter filter, SizeFilter size, LocString label, string id)
    {
        var text = Loc.T(label);
        var width = FilterChip.Width(text, null);
        Place(ref position, left, maximumX, height, width, gap);
        var active = filter.Sizes != SizeFilter.All && (filter.Sizes & size) != 0;
        if (FilterChip.Draw(id, text, null, Styling.AccentMint, active, height))
        {
            var sizes = filter.Sizes == SizeFilter.All ? size : filter.Sizes ^ size;
            State.SetFilter(filter with { Sizes = sizes == SizeFilter.None ? SizeFilter.All : sizes });
        }

        Advance(ref position, width, gap);
    }

    private void DrawStatusChip(ref Vector2 position, float left, float maximumX, float height, float gap, PlotStatus status, string id)
    {
        var filter = State.Filter;
        var text = FindText.Status(status);
        var width = FilterChip.Width(text, null);
        Place(ref position, left, maximumX, height, width, gap);
        var bit = PlotFilter.ToFilter(status);
        if (FilterChip.Draw(id, text, null, StatusMarks.Color(status), (filter.Statuses & bit) != 0, height, tooltip: FindText.StatusHelp(status)))
        {
            State.SetFilter(filter with { Statuses = filter.Statuses ^ bit });
        }

        Advance(ref position, width, gap);
    }

    private static void Place(ref Vector2 position, float left, float maximumX, float height, float width, float gap)
    {
        if (position.X > left && position.X + width > maximumX)
        {
            position = new Vector2(left, position.Y + height + gap);
        }

        ImGui.SetCursorScreenPos(position);
    }

    private static void Advance(ref Vector2 position, float width, float gap) => position.X += width + gap;

    private void DrawViewBar(FindView view, DateTime now)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = Layout.SegmentHeight * scale;
        var gap = GroupGap * scale;
        var views = (ReadOnlySpan<Segmented.Item>)[new(FontAwesomeIcon.Map, Loc.T(L.Find.ViewMap)), new(FontAwesomeIcon.List, Loc.T(L.Find.ViewList))];
        var selected = State.ListView ? 1 : 0;
        if (Segmented.Draw("##ahl_find_view", views, ref selected, height: Layout.SegmentHeight, width: ViewSwitchWidth * scale))
        {
            State.SetListView(selected == 1);
        }

        var x = origin.X + ViewSwitchWidth * scale + gap;
        if (State.ListView)
        {
            for (var index = 0; index < sortOrder.Length; index++)
            {
                sortLabels[index] = FindText.Sort(sortOrder[index]);
            }

            var sort = Array.IndexOf(sortOrder, State.Sort);
            ImGui.SetCursorScreenPos(new Vector2(x, origin.Y + (height - ImGui.GetFrameHeight()) * 0.5f));
            if (Dropdown.Draw("##ahl_find_sort", sortLabels, ref sort, SortWidth))
            {
                State.SetSort(sortOrder[sort]);
            }

            x += SortWidth * scale + gap;
        }

        var refreshRight = DrawRefresh(origin, width, height, now);
        var results = shownResults;
        var summary = summaryText.Get(((long)results.ReportedVacant << 32) | (uint)results.EntryOpen,
            static key => Loc.T(L.Find.Summary, ((int)(key >> 32)).ToString("N0", Loc.Culture), ((int)(key & uint.MaxValue)).ToString("N0", Loc.Culture)));
        using (Fonts.PushCaption())
        {
            var space = refreshRight - gap - x;
            if (space > 0f)
            {
                TextDraw.At(TextDraw.Truncate(summary, space), new Vector2(x, origin.Y + (height - TextDraw.LineHeight()) * 0.5f), Styling.TextDim);
            }
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
    }

    // Returns the left edge of what it drew, so the summary stops short of it.
    private float DrawRefresh(Vector2 origin, float width, float height, DateTime now)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var world = Discovery.World;
        var status = Discovery.Feed.Snapshot.StatusOf(world);
        var wait = world == 0 ? TimeSpan.Zero : Discovery.Feed.ManualWait(world, now);
        var waiting = wait > TimeSpan.Zero;
        var label = waiting ? refreshText.Get((long)Math.Ceiling(wait.TotalSeconds), static seconds => Loc.T(L.Find.RefreshIn, seconds)) : Loc.T(L.Find.Refresh);
        var enabled = world != 0 && !waiting && !status.Loading;
        var buttonWidth = PillButton.Width(label, FontAwesomeIcon.SyncAlt);
        var buttonX = origin.X + width - buttonWidth;
        ImGui.SetCursorScreenPos(new Vector2(buttonX, origin.Y + (height - Layout.ActionPillHeight * scale) * 0.5f));
        if (PillButton.Draw("##ahl_find_refresh", label, Styling.AccentBlue, PillButton.Emphasis.Ghost, FontAwesomeIcon.SyncAlt, enabled, Layout.ActionPillHeight, Loc.T(L.Find.RefreshTooltip)))
        {
            Discovery.Feed.RefreshNow(world, now);
        }

        var (text, color) = AgeLabel(status, now);
        using (Fonts.PushCaption())
        {
            var textWidth = TextDraw.Measure(text).X;
            var textX = buttonX - ItemGap * scale - textWidth;
            TextDraw.At(text, new Vector2(textX, origin.Y + (height - TextDraw.LineHeight()) * 0.5f), color);
            if (status.LiveReceivedUtc != default && ImGui.IsMouseHoveringRect(new Vector2(textX, origin.Y), new Vector2(buttonX, origin.Y + height)))
            {
                Tooltip.Show(FindText.LocalTime(status.LiveUpstreamUtc));
            }

            return textX;
        }
    }

    private (string Text, Vector4 Color) AgeLabel(in FeedWorldStatus status, DateTime now)
    {
        if (status.Loading && !status.HasData)
        {
            return (Loc.T(L.Find.ListingsLoading), Styling.TextDim);
        }

        if (status.LiveReceivedUtc != default)
        {
            var key = status.LiveUpstreamUtc.Ticks ^ ((long)(now - status.LiveUpstreamUtc).TotalMinutes << 48);
            if (!ageText.TryGet(key, out var text))
            {
                text = ageText.Set(key, Loc.T(L.Find.ListingsRead, Formatting.RelativeTime(status.LiveUpstreamUtc, now)));
            }

            return (text, status.Failure == FeedFailure.None ? Styling.TextDim : Styling.AccentAmberSoft);
        }

        if (status.ShowingSavedCopy)
        {
            if (!ageText.TryGet(status.CachedReceivedUtc.Ticks, out var saved))
            {
                saved = ageText.Set(status.CachedReceivedUtc.Ticks, Loc.T(L.Find.ListingsSaved, FindText.LocalTime(status.CachedReceivedUtc)));
            }

            return (saved, Styling.AccentAmberSoft);
        }

        return status.Failure != FeedFailure.None ? (Loc.T(L.Find.ListingsFailed), Styling.AccentRose) : (string.Empty, Styling.TextDim);
    }
}
