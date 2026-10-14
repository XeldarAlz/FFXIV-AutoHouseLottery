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

// Find a home: the area and filter bar, the map or the list of matching plots, and a drawer that holds the shortlist or
// one plot's details. Everything drawn comes from the latest immutable view the Find state built on a task; while the
// pointer rests on the results, a refreshed view waits so rows do not move under a click.
internal sealed partial class FindHomePage
{
    private const float SectionGap = 10f;
    private const float DrawerGap = 16f;
    private const float DrawerMinWidth = 250f;
    private const float DrawerMaxWidth = 340f;
    private const float DrawerShare = 0.34f;
    private const float BottomBarHeight = 40f;
    private const float PanelPad = 10f;
    private const string ResultsId = "##ahl_find_results";

    private readonly Plugin plugin;
    private FindResults shownResults = FindResults.Empty;
    private int shownQueryRevision = -1;
    private AreaScope labelledArea;
    private LanguageInfo? labelledLanguage;
    private bool labelledNames;
    private string scopeLabel = string.Empty;
    private string scopeTooltip = string.Empty;
    private string areasLabel = string.Empty;
    private bool resultsHovered;
    private PlotKey? details;
    private TransientNotice notice;
    private CachedText summaryText;
    private CachedText addAllText;

    public FindHomePage(Plugin plugin)
    {
        this.plugin = plugin;
    }

    private Discovery Discovery => plugin.Discovery;

    private FindState State => Discovery.Find;

    public void Draw()
    {
        var view = State.View;
        UpdateShownResults(view);
        var now = DateTime.UtcNow;
        var scale = ImGuiHelpers.GlobalScale;

        DrawContextRow();
        Styling.VSpace(SectionGap);
        DrawFilterRow();
        Styling.VSpace(SectionGap);
        DrawViewBar(view, now);
        Styling.VSpace(SectionGap);

        var available = ImGui.GetContentRegionAvail();
        var drawerWidth = Math.Clamp(available.X * DrawerShare, DrawerMinWidth * scale, DrawerMaxWidth * scale);
        var gap = DrawerGap * scale;
        var resultsWidth = MathF.Max(1f, available.X - drawerWidth - gap);
        var height = MathF.Max(1f, available.Y - 1f);
        var origin = ImGui.GetCursorScreenPos();
        DrawResults(view, new Vector2(resultsWidth, height), now);
        ImGui.SetCursorScreenPos(origin + new Vector2(resultsWidth + gap, 0f));
        DrawDrawer(view, new Vector2(drawerWidth, height), now);
    }

    // A refresh never reorders the rows under the pointer; the player's own filter changes always apply at once.
    private void UpdateShownResults(FindView view)
    {
        if (ReferenceEquals(view.Results, shownResults))
        {
            return;
        }

        var refreshOnly = view.QueryRevision == shownQueryRevision;
        if (refreshOnly && resultsHovered && shownResults.Count > 0)
        {
            return;
        }

        shownResults = view.Results;
        shownQueryRevision = view.QueryRevision;
    }

    // The labels describing an area change only with the area, the language or the district names arriving.
    private (string Scope, string Tooltip, string Areas) ScopeLabels(in AreaScope area)
    {
        var scoped = area with { WorldId = 0 };
        if (scoped != labelledArea || !ReferenceEquals(labelledLanguage, Loc.Current) || labelledNames != Discovery.Names.Ready)
        {
            labelledArea = scoped;
            labelledLanguage = Loc.Current;
            labelledNames = Discovery.Names.Ready;
            scopeLabel = FindText.Scope(Discovery.Names, scoped);
            scopeTooltip = Loc.T(L.Find.AddAllTooltip, scopeLabel);
            areasLabel = Loc.T(L.Find.AreasChip, FindText.Districts(Discovery.Names, ScopeSummary.Describe(scoped)));
        }

        return (scopeLabel, scopeTooltip, areasLabel);
    }

    private void DrawResults(FindView view, Vector2 size, DateTime now)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        Paint.Surface(drawList, origin, origin + size, Styling.CardRounding * scale, Styling.WithAlpha(Styling.Surface0, 0.7f), Styling.WithAlpha(Styling.BorderDim, 0.5f));
        var barHeight = BottomBarHeight * scale;
        var bodySize = new Vector2(size.X, MathF.Max(1f, size.Y - barHeight));
        resultsHovered = ImGui.IsMouseHoveringRect(origin, origin + bodySize);

        using (ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(PanelPad, PanelPad) * scale))
        using (var child = ImRaii.Child(ResultsId, bodySize, false, ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (child)
            {
                DrawResultsBody(view, now);
            }
        }

        ImGui.SetCursorScreenPos(origin + new Vector2(0f, bodySize.Y));
        DrawAddAllBar(new Vector2(size.X, barHeight));
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);
    }

    private void DrawResultsBody(FindView view, DateTime now)
    {
        var world = Discovery.World;
        var status = Discovery.Feed.Snapshot.StatusOf(world);
        if (world == 0)
        {
            Message(FontAwesomeIcon.Globe, Loc.T(L.Find.NoWorld));
            return;
        }

        if (!status.HasData)
        {
            if (status.Failure != Core.Providers.Model.FeedFailure.None && !status.Loading)
            {
                Message(FontAwesomeIcon.ExclamationTriangle, Loc.T(L.Find.Failed));
                return;
            }

            Message(FontAwesomeIcon.Hourglass, Loc.T(L.Find.Loading, Discovery.Names.World(world)));
            return;
        }

        if (status.ShowingSavedCopy)
        {
            DrawBanner(Loc.T(L.Find.SavedCopyBanner, FindText.LocalTime(status.CachedReceivedUtc)));
        }

        if (State.Area.SelectsNothing)
        {
            Message(FontAwesomeIcon.MapMarkedAlt, Loc.T(L.Find.NoArea));
            return;
        }

        if (State.ListView)
        {
            if (shownResults.Count == 0)
            {
                DrawNoMatches();
                return;
            }

            DrawList(view, now);
            return;
        }

        DrawMap(view, now);
    }

    private void DrawNoMatches()
    {
        var noCoverage = shownResults.ReportedVacant == 0;
        Message(noCoverage ? FontAwesomeIcon.SatelliteDish : FontAwesomeIcon.Filter, Loc.T(noCoverage ? L.Find.NoCoverage : L.Find.NoMatches));
        if (noCoverage)
        {
            return;
        }

        Styling.VSpace(8f);
        var label = Loc.T(L.Find.ClearFilters);
        Styling.CenterNextItem(PillButton.Width(label, FontAwesomeIcon.Undo));
        if (PillButton.Draw("##ahl_find_clear_filters", label, Styling.AccentGlow, PillButton.Emphasis.Tinted, FontAwesomeIcon.Undo))
        {
            State.SetFilter(PlotFilter.Default with { Buyer = State.Filter.Buyer });
        }
    }

    private void DrawAddAllBar(Vector2 size)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        Paint.Hairline(drawList, origin + new Vector2(PanelPad * scale, 0f), origin + new Vector2(size.X - PanelPad * scale, 0f));
        var results = shownResults;
        var count = results.Count;
        var label = addAllText.Get(count, static value => Loc.T(L.Find.AddAllMatching, value.ToString("N0", Loc.Culture)));
        var pad = PanelPad * scale;
        var buttonHeight = Layout.ActionPillHeight;
        var buttonY = origin.Y + (size.Y - buttonHeight * scale) * 0.5f;
        ImGui.SetCursorScreenPos(new Vector2(origin.X + pad, buttonY));
        var (scope, tooltip, _) = ScopeLabels(results.Query.Area);
        var enabled = count > 0 && !results.Query.Area.IsEmpty;
        if (PillButton.Draw("##ahl_find_add_all", label, Styling.AccentGlow, PillButton.Emphasis.Filled, FontAwesomeIcon.PlusCircle, enabled, buttonHeight, tooltip))
        {
            var added = State.AddAll(results.MatchingKeys, "Add all matching");
            notice.Show(Loc.Plural(L.Find.Added, added), Styling.AccentMint);
        }

        var textX = origin.X + pad + PillButton.Width(label, FontAwesomeIcon.PlusCircle) + pad;
        using (Fonts.PushCaption())
        {
            var noticeWidth = notice.DrawRight(origin.X + size.X - pad, origin.Y + size.Y * 0.5f, size.X * 0.3f);
            var scopeText = TextDraw.Truncate(scope, origin.X + size.X - pad - noticeWidth - pad - textX);
            TextDraw.At(scopeText, new Vector2(textX, origin.Y + (size.Y - TextDraw.LineHeight()) * 0.5f), Styling.TextDim);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);
    }

    private static void Message(FontAwesomeIcon icon, string text)
    {
        Styling.VSpace(24f);
        EmptyState.Draw(icon, text);
    }

    private static void DrawBanner(string text)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        using (Fonts.PushCaption())
        {
            var height = TextDraw.LineHeight() + 10f * scale;
            var end = origin + new Vector2(width, height);
            Paint.Pill(ImGui.GetWindowDrawList(), origin, end, Styling.WithAlpha(Styling.AccentAmber, 0.14f), Styling.WithAlpha(Styling.AccentAmber, 0.45f));
            var iconSize = TextDraw.IconSize(FontAwesomeIcon.History);
            TextDraw.Icon(FontAwesomeIcon.History, new Vector2(origin.X + 10f * scale, origin.Y + (height - iconSize.Y) * 0.5f), Styling.AccentAmberSoft);
            var textX = origin.X + 16f * scale + iconSize.X;
            TextDraw.At(TextDraw.Truncate(text, end.X - textX - 10f * scale), new Vector2(textX, origin.Y + 5f * scale), Styling.AccentAmberSoft);
            ImGui.Dummy(new Vector2(width, height));
        }

        Styling.VSpace(6f);
    }
}
