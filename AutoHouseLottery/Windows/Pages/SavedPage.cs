using AutoHouseLottery.Core.Find;
using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Core.Saved;
using AutoHouseLottery.Core.Saved.Model;
using AutoHouseLottery.Windows.Components;
using AutoHouseLottery.Windows.Find;
using AutoHouseLottery.Windows.Shell;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System.Collections.Immutable;
using System.Numerics;
using System.Text;

namespace AutoHouseLottery.Windows.Pages;

// Saved searches, fixed plot lists and the notices they raised. A search fills the shortlist with what matches now; a
// list fills it with exactly its plots.
internal sealed class SavedPage
{
    private const float CardPad = 14f;
    private const float CardGap = 10f;
    private const float RowPad = 4f;
    private const float RemoveSize = 20f;
    private const float MarkRadius = 5f;
    private const int ConfirmWindowMs = 3_000;
    private const int SearchIdSpan = 100_000;

    private readonly Plugin plugin;
    private readonly Action<AppWindow.Page> navigate;
    private readonly HashSet<Guid> expanded = [];
    private readonly Dictionary<Guid, string> summaries = [];
    private readonly Dictionary<Guid, float> cardHeights = [];
    private (int Revision, LanguageInfo? Language, bool Named) summariesFor;
    private readonly List<string> noticeTexts = [];
    private (int Revision, LanguageInfo? Language, bool Named) noticeTextsFor;
    private Guid armedDelete;
    private long armedUntil;
    private TransientNotice notice;

    public SavedPage(Plugin plugin, Action<AppWindow.Page> navigate)
    {
        this.plugin = plugin;
        this.navigate = navigate;
    }

    private Discovery Discovery => plugin.Discovery;

    private SavedService Saved => Discovery.Saved;

    public void Draw()
    {
        PageHeader.Draw(Loc.T(L.Saved.Title), Loc.T(L.Saved.Subtitle));
        DrawNotifyRow();
        notice.DrawLine(ImGui.GetContentRegionAvail().X);
        Styling.VSpace(CardGap);
        var library = Saved.Library;
        if (library.IsEmpty && Saved.Recent.Count == 0)
        {
            EmptyState.Draw(FontAwesomeIcon.Bookmark, Loc.T(L.Saved.Empty));
            return;
        }

        RefreshSummaries();
        var now = DateTime.UtcNow;
        DrawNotices(now);
        if (library.Searches.Count > 0)
        {
            Section(Loc.T(L.Saved.Searches), Loc.T(L.Saved.SearchesHelp));
            for (var index = 0; index < library.Searches.Count; index++)
            {
                DrawSearch(library.Searches[index], index);
            }
        }

        if (library.Lists.Count > 0)
        {
            Section(Loc.T(L.Saved.Lists), Loc.T(L.Saved.ListsHelp));
            var view = Discovery.Find.View;
            for (var index = 0; index < library.Lists.Count; index++)
            {
                DrawList(library.Lists[index], index, view);
            }
        }
    }

    private void DrawNotifyRow()
    {
        var configuration = plugin.Configuration;
        var notify = configuration.NotifySaved;
        if (ToggleSwitch.Draw("##ahl_saved_notify", ref notify))
        {
            configuration.NotifySaved = notify;
            configuration.Save();
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(Loc.T(L.Saved.Notify));
        if (ImGui.IsItemHovered())
        {
            Tooltip.Show(Loc.T(L.Saved.NotifyHelp));
        }
    }

    private void DrawNotices(DateTime now)
    {
        var recent = Saved.Recent;
        if (recent.Count == 0)
        {
            return;
        }

        Section(Loc.T(L.Saved.Notices), null);
        if (PillButton.Draw("##ahl_saved_clear_notices", Loc.T(L.Saved.ClearNotices), Styling.AccentRose, PillButton.Emphasis.Ghost, FontAwesomeIcon.Broom))
        {
            Saved.ClearRecent();
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        for (var index = 0; index < recent.Count; index++)
        {
            var record = recent[index];
            var origin = ImGui.GetCursorScreenPos();
            var width = ImGui.GetContentRegionAvail().X;
            var icon = record.Kind switch
            {
                NoticeKind.NewMatch => FontAwesomeIcon.Search,
                NoticeKind.EntryOpened => FontAwesomeIcon.DoorOpen,
                NoticeKind.EntryClosing => FontAwesomeIcon.Hourglass,
                _ => FontAwesomeIcon.Trophy,
            };
            var lineHeight = TextDraw.LineHeight();
            var iconSize = TextDraw.IconSize(icon);
            TextDraw.Icon(icon, new Vector2(origin.X, origin.Y + (lineHeight - iconSize.Y) * 0.5f), Styling.AccentGlowSoft);
            var age = Formatting.RelativeTime(record.AtUtc, now);
            var ageWidth = TextDraw.Measure(age).X;
            var textX = origin.X + iconSize.X + 10f * scale;
            var text = NoticeText(index);
            TextDraw.At(TextDraw.Truncate(text, width - (textX - origin.X) - ageWidth - 12f * scale), new Vector2(textX, origin.Y), Styling.TextSecondary);
            TextDraw.At(age, new Vector2(origin.X + width - ageWidth, origin.Y), Styling.TextMuted);
            ImGui.Dummy(new Vector2(width, lineHeight + RowPad * scale));
        }

        Styling.VSpace(CardGap);
    }

    private void DrawSearch(SavedSearch search, int index)
    {
        var matches = Saved.Matches.TryGetValue(search.Id, out var found) ? found : ImmutableArray<PlotKey>.Empty;
        ImGui.PushID(index);
        using (new Card(cardHeights, search.Id, CardPad))
        {
            Title(search.Name, search.CreatedUtc);
            TextDraw.Hint(summaries.TryGetValue(search.Id, out var summary) ? summary : string.Empty);
            if (!found.IsDefault)
            {
                ImGui.TextUnformatted(Loc.Plural(L.Saved.MatchingNow, matches.Length));
            }

            Styling.VSpace(4f);
            if (PillButton.Draw("##show", Loc.T(L.Saved.ShowOnFind), Styling.AccentBlue, PillButton.Emphasis.Tinted, FontAwesomeIcon.MapMarkedAlt))
            {
                Discovery.Find.ApplySearch(search.Area, search.Filter);
                navigate(AppWindow.Page.FindHome);
            }

            ImGui.SameLine();
            if (PillButton.Draw("##add", Loc.T(L.Saved.AddMatches, matches.Length), Styling.AccentGlow, PillButton.Emphasis.Filled, FontAwesomeIcon.PlusCircle, matches.Length > 0))
            {
                var added = Discovery.Find.AddAll(matches, $"Saved search \"{search.Name}\"");
                notice.Show(Loc.Plural(L.Find.Added, added), Styling.AccentMint);
            }

            ImGui.SameLine();
            DeleteButton(search.Id);
        }

        ImGui.PopID();
    }

    private void DrawList(SavedList list, int index, FindView view)
    {
        ImGui.PushID(SearchIdSpan + index);
        using (new Card(cardHeights, list.Id, CardPad))
        {
            Title(list.Name, list.CreatedUtc);
            var open = 0;
            for (var plotIndex = 0; plotIndex < list.Plots.Length; plotIndex++)
            {
                if (view.StatusOf(list.Plots[plotIndex]) == PlotStatus.EntryOpen)
                {
                    open++;
                }
            }

            TextDraw.Hint(Loc.T(L.Find.CountsJoin, Loc.Plural(L.Find.PlotCount, list.Plots.Length), Loc.Plural(L.Saved.EntryOpenNow, open)));
            Styling.VSpace(4f);
            if (PillButton.Draw("##add", Loc.T(L.Saved.AddPlots, list.Plots.Length), Styling.AccentGlow, PillButton.Emphasis.Filled, FontAwesomeIcon.PlusCircle, list.Plots.Length > 0))
            {
                var added = Discovery.Find.AddAll(list.Plots, $"Saved list \"{list.Name}\"");
                notice.Show(Loc.Plural(L.Find.Added, added), Styling.AccentMint);
            }

            ImGui.SameLine();
            var isExpanded = expanded.Contains(list.Id);
            if (PillButton.Draw("##expand", Loc.T(isExpanded ? L.Saved.HidePlots : L.Saved.ShowPlots), Styling.AccentBlue, PillButton.Emphasis.Ghost, isExpanded ? FontAwesomeIcon.ChevronUp : FontAwesomeIcon.ChevronDown))
            {
                if (!expanded.Remove(list.Id))
                {
                    expanded.Add(list.Id);
                }
            }

            ImGui.SameLine();
            DeleteButton(list.Id);
            if (isExpanded)
            {
                Styling.VSpace(4f);
                DrawListPlots(list, view);
            }
        }

        ImGui.PopID();
    }

    private void DrawListPlots(SavedList list, FindView view)
    {
        if (list.Plots.IsEmpty)
        {
            TextDraw.Hint(Loc.T(L.Saved.EmptyList));
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        for (var index = 0; index < list.Plots.Length; index++)
        {
            var key = list.Plots[index];
            var status = view.StatusOf(key);
            var origin = ImGui.GetCursorScreenPos();
            var width = ImGui.GetContentRegionAvail().X;
            var height = TextDraw.LineHeight() + RowPad * 2f * scale;
            var radius = MarkRadius * scale;
            var midY = origin.Y + height * 0.5f;
            StatusMarks.Draw(drawList, new Vector2(origin.X + radius, midY), radius, status);
            var label = FindText.Status(status);
            var labelWidth = TextDraw.Measure(label).X;
            var removeSize = RemoveSize * scale;
            var textX = origin.X + radius * 2f + 8f * scale;
            var textY = midY - TextDraw.LineHeight() * 0.5f;
            TextDraw.At(TextDraw.Truncate(PlotLabels.AddressOnWorld(Discovery.Names, key), width - (textX - origin.X) - labelWidth - removeSize - 20f * scale), new Vector2(textX, textY), Styling.TextSecondary);
            TextDraw.At(label, new Vector2(origin.X + width - removeSize - 8f * scale - labelWidth, textY), StatusMarks.Color(status));
            ImGui.SetCursorScreenPos(new Vector2(origin.X + width - removeSize, origin.Y + (height - removeSize) * 0.5f));
            ImGui.PushID(index);
            if (IconButton.Draw(FontAwesomeIcon.Times, "##remove", removeSize, Styling.TextDim, Loc.T(L.Find.Remove)))
            {
                Saved.RemoveFromList(list.Id, key);
            }

            ImGui.PopID();
            ImGui.SetCursorScreenPos(origin);
            ImGui.Dummy(new Vector2(width, height));
        }
    }

    private string NoticeText(int index)
    {
        var current = (Saved.RecentRevision, Loc.Current, Discovery.Names.Ready);
        if (noticeTextsFor != current)
        {
            noticeTextsFor = current;
            noticeTexts.Clear();
        }

        while (noticeTexts.Count <= index)
        {
            var next = Saved.Recent[noticeTexts.Count];
            noticeTexts.Add(NoticeAnnouncer.Describe(Discovery.Names, next.Kind, next.Plot, next.SourceName));
        }

        return noticeTexts[index];
    }

    private void DeleteButton(Guid id)
    {
        var armed = armedDelete == id && Environment.TickCount64 < armedUntil;
        if (!PillButton.Draw("##delete", Loc.T(armed ? L.Saved.ConfirmDelete : L.Saved.Delete), Styling.AccentRose, armed ? PillButton.Emphasis.Tinted : PillButton.Emphasis.Ghost, FontAwesomeIcon.Trash))
        {
            return;
        }

        if (armed)
        {
            Saved.Remove(id);
            armedDelete = Guid.Empty;
            return;
        }

        armedDelete = id;
        armedUntil = Environment.TickCount64 + ConfirmWindowMs;
    }

    private static void Title(string name, DateTime createdUtc)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        float height;
        using (Fonts.PushHeadline())
        {
            height = TextDraw.LineHeight();
            TextDraw.At(TextDraw.Truncate(name, width * 0.7f), origin, Styling.TextStrong);
        }

        if (createdUtc != default)
        {
            using (Fonts.PushCaption())
            {
                var saved = Loc.T(L.Saved.SavedOn, createdUtc.ToLocalTime().ToString("d", Loc.Culture));
                TextDraw.Right(saved, origin.X + width, origin.Y + (height - TextDraw.LineHeight()) * 0.5f, Styling.TextMuted);
            }
        }

        ImGui.Dummy(new Vector2(width, height));
    }

    private static void Section(string title, string? help)
    {
        Styling.SectionLabel(title);
        if (help is not null)
        {
            TextDraw.Hint(help);
        }

        Styling.VSpace(4f);
    }

    private void RefreshSummaries()
    {
        var current = (Saved.Library.Revision, Loc.Current, Discovery.Names.Ready);
        if (summariesFor == current)
        {
            return;
        }

        summariesFor = current;
        summaries.Clear();
        var searches = Saved.Library.Searches;
        for (var index = 0; index < searches.Count; index++)
        {
            summaries[searches[index].Id] = Summary(searches[index]);
        }
    }

    private string Summary(SavedSearch search)
    {
        var text = new StringBuilder();
        text.Append(Discovery.Names.World(search.Area.WorldId)).Append(TextDraw.Separator).Append(FindText.Scope(Discovery.Names, search.Area));
        var filter = search.Filter;
        for (var status = PlotStatus.EntryOpen; status <= PlotStatus.NeedsRefresh; status++)
        {
            if ((filter.Statuses & PlotFilter.ToFilter(status)) != 0)
            {
                text.Append(TextDraw.Separator).Append(FindText.Status(status));
            }
        }

        if (filter.Sizes != SizeFilter.All)
        {
            for (var size = PlotSize.Small; size <= PlotSize.Large; size++)
            {
                if ((filter.Sizes & PlotFilter.ToFilter(size)) != 0)
                {
                    text.Append(TextDraw.Separator).Append(FindText.Size(size));
                }
            }
        }

        if (filter.PriceCeiling > 0)
        {
            text.Append(TextDraw.Separator).Append(Loc.T(L.Saved.PriceUpTo, Formatting.Gil(filter.PriceCeiling)));
        }

        return text.ToString();
    }

    // A card is a child window, so its padding holds on both sides; its height is the content measured on the previous
    // frame, which settles after the first frame a card is shown.
    private readonly ref struct Card
    {
        private const float FirstHeight = 120f;
        private const string ChildId = "##ahl_saved_card";

        private readonly Dictionary<Guid, float> heights;
        private readonly Guid id;
        private readonly float pad;
        private readonly Vector2 origin;
        private readonly float width;
        private readonly IDisposable style;

        public Card(Dictionary<Guid, float> heights, Guid id, float padding)
        {
            this.heights = heights;
            this.id = id;
            var scale = ImGuiHelpers.GlobalScale;
            pad = padding * scale;
            origin = ImGui.GetCursorScreenPos();
            width = ImGui.GetContentRegionAvail().X;
            var height = heights.TryGetValue(id, out var measured) ? measured : FirstHeight * scale;
            Paint.Surface(ImGui.GetWindowDrawList(), origin, origin + new Vector2(width, height), Styling.CardRounding * scale, Styling.CardBgSoft, Styling.WithAlpha(Styling.BorderDim, 0.55f));
            style = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(pad, pad));
            ImGui.BeginChild(ChildId, new Vector2(width, height), false, ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        }

        public void Dispose()
        {
            heights[id] = ImGui.GetCursorPosY() + pad;
            ImGui.EndChild();
            style.Dispose();
            var scale = ImGuiHelpers.GlobalScale;
            ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + heights[id]));
            ImGui.Dummy(new Vector2(width, CardGap * scale));
        }
    }
}
