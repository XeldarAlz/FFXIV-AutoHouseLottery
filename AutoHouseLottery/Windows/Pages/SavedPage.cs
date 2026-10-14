using AutoHouseLottery.Core.Find;
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
using System.Numerics;

namespace AutoHouseLottery.Windows.Pages;

// Saved searches, fixed plot lists and the notices they raised. A search fills the shortlist with what matches now; a
// list fills it with exactly its plots. Every label comes from a text model rebuilt only when something it shows changes.
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
    private readonly Dictionary<Guid, float> cardHeights = [];
    private readonly SavedText text = new();
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

        text.Refresh(Saved, Discovery.Find.View, Discovery.Names, DateTime.UtcNow);
        DrawNotices();
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
            for (var index = 0; index < library.Lists.Count; index++)
            {
                DrawList(library.Lists[index], index);
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

    private void DrawNotices()
    {
        var notices = text.Notices;
        if (notices.Length == 0)
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
        for (var index = 0; index < notices.Length; index++)
        {
            var line = notices[index];
            var origin = ImGui.GetCursorScreenPos();
            var width = ImGui.GetContentRegionAvail().X;
            var icon = line.Kind switch
            {
                NoticeKind.NewMatch => FontAwesomeIcon.Search,
                NoticeKind.EntryOpened => FontAwesomeIcon.DoorOpen,
                NoticeKind.EntryClosing => FontAwesomeIcon.Hourglass,
                _ => FontAwesomeIcon.Trophy,
            };
            var lineHeight = TextDraw.LineHeight();
            var iconSize = TextDraw.IconSize(icon);
            TextDraw.Icon(icon, new Vector2(origin.X, origin.Y + (lineHeight - iconSize.Y) * 0.5f), Styling.AccentGlowSoft);
            var ageWidth = TextDraw.Measure(line.Age).X;
            var textX = origin.X + iconSize.X + 10f * scale;
            TextDraw.At(TextDraw.Truncate(line.Text, width - (textX - origin.X) - ageWidth - 12f * scale), new Vector2(textX, origin.Y), Styling.TextSecondary);
            TextDraw.At(line.Age, new Vector2(origin.X + width - ageWidth, origin.Y), Styling.TextMuted);
            ImGui.Dummy(new Vector2(width, lineHeight + RowPad * scale));
        }

        Styling.VSpace(CardGap);
    }

    private void DrawSearch(SavedSearch search, int index)
    {
        var labels = text.Search(search.Id);
        ImGui.PushID(index);
        using (new Card(cardHeights, search.Id, CardPad))
        {
            Title(search.Name, labels.SavedOn);
            TextDraw.Hint(labels.Summary);
            if (labels.Matching is { } matching)
            {
                ImGui.TextUnformatted(matching);
            }

            Styling.VSpace(4f);
            if (PillButton.Draw("##show", Loc.T(L.Saved.ShowOnFind), Styling.AccentBlue, PillButton.Emphasis.Tinted, FontAwesomeIcon.MapMarkedAlt))
            {
                Discovery.Find.ApplySearch(search.Area, search.Filter);
                navigate(AppWindow.Page.FindHome);
            }

            ImGui.SameLine();
            if (PillButton.Draw("##add", labels.AddLabel, Styling.AccentGlow, PillButton.Emphasis.Filled, FontAwesomeIcon.PlusCircle, labels.MatchCount > 0)
                && Saved.Matches.TryGetValue(search.Id, out var matches))
            {
                var added = Discovery.Find.AddAll(matches, $"Saved search \"{search.Name}\"");
                notice.Show(Loc.Plural(L.Find.Added, added), Styling.AccentMint);
            }

            ImGui.SameLine();
            DeleteButton(search.Id);
        }

        ImGui.PopID();
    }

    private void DrawList(SavedList list, int index)
    {
        var labels = text.List(list.Id);
        ImGui.PushID(SearchIdSpan + index);
        using (new Card(cardHeights, list.Id, CardPad))
        {
            Title(list.Name, labels.SavedOn);
            TextDraw.Hint(labels.Counts);
            Styling.VSpace(4f);
            if (PillButton.Draw("##add", labels.AddLabel, Styling.AccentGlow, PillButton.Emphasis.Filled, FontAwesomeIcon.PlusCircle, list.Plots.Length > 0))
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
                DrawListPlots(list.Id, labels.Plots);
            }
        }

        ImGui.PopID();
    }

    private void DrawListPlots(Guid listId, ListPlotText[] plots)
    {
        if (plots.Length == 0)
        {
            TextDraw.Hint(Loc.T(L.Saved.EmptyList));
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        for (var index = 0; index < plots.Length; index++)
        {
            var plot = plots[index];
            var origin = ImGui.GetCursorScreenPos();
            var width = ImGui.GetContentRegionAvail().X;
            var height = TextDraw.LineHeight() + RowPad * 2f * scale;
            var radius = MarkRadius * scale;
            var midY = origin.Y + height * 0.5f;
            StatusMarks.Draw(drawList, new Vector2(origin.X + radius, midY), radius, plot.Status);
            var labelWidth = TextDraw.Measure(plot.StatusLabel).X;
            var removeSize = RemoveSize * scale;
            var textX = origin.X + radius * 2f + 8f * scale;
            var textY = midY - TextDraw.LineHeight() * 0.5f;
            TextDraw.At(TextDraw.Truncate(plot.Address, width - (textX - origin.X) - labelWidth - removeSize - 20f * scale), new Vector2(textX, textY), Styling.TextSecondary);
            TextDraw.At(plot.StatusLabel, new Vector2(origin.X + width - removeSize - 8f * scale - labelWidth, textY), StatusMarks.Color(plot.Status));
            ImGui.SetCursorScreenPos(new Vector2(origin.X + width - removeSize, origin.Y + (height - removeSize) * 0.5f));
            ImGui.PushID(index);
            if (IconButton.Draw(FontAwesomeIcon.Times, "##remove", removeSize, Styling.TextDim, Loc.T(L.Find.Remove)))
            {
                Saved.RemoveFromList(listId, plot.Key);
            }

            ImGui.PopID();
            ImGui.SetCursorScreenPos(origin);
            ImGui.Dummy(new Vector2(width, height));
        }
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

    private static void Title(string name, string savedOn)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        float height;
        using (Fonts.PushHeadline())
        {
            height = TextDraw.LineHeight();
            TextDraw.At(TextDraw.Truncate(name, width * 0.7f), origin, Styling.TextStrong);
        }

        if (savedOn.Length > 0)
        {
            using (Fonts.PushCaption())
            {
                TextDraw.Right(savedOn, origin.X + width, origin.Y + (height - TextDraw.LineHeight()) * 0.5f, Styling.TextMuted);
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
