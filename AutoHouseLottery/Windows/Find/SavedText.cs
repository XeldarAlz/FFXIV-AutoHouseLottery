using AutoHouseLottery.Core.Find;
using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Core.Maps;
using AutoHouseLottery.Core.Saved;
using AutoHouseLottery.Core.Saved.Model;
using System.Text;

namespace AutoHouseLottery.Windows.Find;

internal sealed record SearchText(string Summary, string? Matching, string AddLabel, int MatchCount, string SavedOn);

internal sealed record ListText(string Counts, string AddLabel, string SavedOn, ListPlotText[] Plots);

internal readonly record struct ListPlotText(PlotKey Key, PlotStatus? Status, string Address, string StatusLabel);

internal readonly record struct NoticeText(NoticeKind Kind, string Text, string Age);

// The Saved page's words, and the status of every saved plot, worked out once per change of the library, its matches,
// the Find a home view, the notices, the minute or the language rather than on every frame.
internal sealed class SavedText
{
    private readonly Dictionary<Guid, SearchText> searches = [];
    private readonly Dictionary<Guid, ListText> lists = [];
    private (int Library, int Matches, int View, int Notices, long Minute, LanguageInfo? Language, bool Named) builtFor = (-1, -1, -1, -1, 0, null, false);

    public NoticeText[] Notices { get; private set; } = [];

    public SearchText Search(Guid id) => searches[id];

    public ListText List(Guid id) => lists[id];

    public void Refresh(SavedService saved, FindView view, GameNames names, DateTime nowUtc)
    {
        var inputs = (saved.Library.Revision, saved.MatchesRevision, view.Revision, saved.RecentRevision, nowUtc.Ticks / TimeSpan.TicksPerMinute, Loc.Current, names.Ready);
        if (inputs == builtFor)
        {
            return;
        }

        builtFor = inputs;
        searches.Clear();
        for (var index = 0; index < saved.Library.Searches.Count; index++)
        {
            var search = saved.Library.Searches[index];
            var known = saved.Matches.TryGetValue(search.Id, out var matches);
            var count = known ? matches.Length : 0;
            searches[search.Id] = new SearchText(
                Summary(search, names),
                known ? Loc.Plural(L.Saved.MatchingNow, count) : null,
                Loc.T(L.Saved.AddMatches, count),
                count,
                SavedOn(search.CreatedUtc));
        }

        lists.Clear();
        for (var index = 0; index < saved.Library.Lists.Count; index++)
        {
            var list = saved.Library.Lists[index];
            lists[list.Id] = ListOf(list, view, names);
        }

        var recent = saved.Recent;
        var notices = new NoticeText[recent.Count];
        for (var index = 0; index < notices.Length; index++)
        {
            var record = recent[index];
            notices[index] = new NoticeText(record.Kind, NoticeAnnouncer.Describe(names, record.Kind, record.Plot, record.SourceName), Formatting.RelativeTime(record.AtUtc, nowUtc));
        }

        Notices = notices;
    }

    private static ListText ListOf(SavedList list, FindView view, GameNames names)
    {
        var plots = new ListPlotText[list.Plots.Length];
        var open = 0;
        for (var index = 0; index < plots.Length; index++)
        {
            var key = list.Plots[index];
            var status = view.StatusOf(key);
            if (status == PlotStatus.EntryOpen)
            {
                open++;
            }

            plots[index] = new ListPlotText(key, status, PlotLabels.AddressOnWorld(names, key), FindText.Status(status));
        }

        return new ListText(
            Loc.T(L.Find.CountsJoin, Loc.Plural(L.Find.PlotCount, plots.Length), Loc.Plural(L.Saved.EntryOpenNow, open)),
            Loc.T(L.Saved.AddPlots, plots.Length),
            SavedOn(list.CreatedUtc),
            plots);
    }

    private static string SavedOn(DateTime createdUtc) => createdUtc == default ? string.Empty : Loc.T(L.Saved.SavedOn, createdUtc.ToLocalTime().ToString("d", Loc.Culture));

    private static string Summary(SavedSearch search, GameNames names)
    {
        var text = new StringBuilder();
        text.Append(names.World(search.Area.WorldId)).Append(TextDraw.Separator).Append(FindText.Scope(names, search.Area));
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
}
