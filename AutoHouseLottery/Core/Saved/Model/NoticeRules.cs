using System.Collections.Immutable;
using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Saved.Model;

internal static class NoticeRules
{
    public const int ClosingLeadMinutes = 30;

    private static readonly TimeSpan closingLead = TimeSpan.FromMinutes(ClosingLeadMinutes);

    public static ImmutableArray<Notice> Due(in SavedLibrarySnapshot library, IReadOnlyDictionary<PlotKey, PlotSnapshot> plots, in CyclePosition position, DateTime nowUtc, NoticeLedger ledger)
    {
        var due = ImmutableArray.CreateBuilder<Notice>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < library.Searches.Length; index++)
        {
            CollectSearch(library.Searches[index], plots, position, nowUtc, ledger, keys, due);
        }

        for (var index = 0; index < library.Lists.Length; index++)
        {
            CollectList(library.Lists[index], plots, position, nowUtc, ledger, keys, due);
        }

        return due.ToImmutable();
    }

    public static ImmutableArray<Notice> DueForSearch(SavedSearch search, IReadOnlyDictionary<PlotKey, PlotSnapshot> plots, in CyclePosition position, DateTime nowUtc, NoticeLedger ledger)
    {
        var due = ImmutableArray.CreateBuilder<Notice>();
        CollectSearch(search, plots, position, nowUtc, ledger, new HashSet<string>(StringComparer.Ordinal), due);
        return due.ToImmutable();
    }

    public static ImmutableArray<Notice> DueForList(SavedList list, IReadOnlyDictionary<PlotKey, PlotSnapshot> plots, in CyclePosition position, DateTime nowUtc, NoticeLedger ledger)
    {
        var due = ImmutableArray.CreateBuilder<Notice>();
        CollectList(list, plots, position, nowUtc, ledger, new HashSet<string>(StringComparer.Ordinal), due);
        return due.ToImmutable();
    }

    private static void CollectSearch(
        SavedSearch search,
        IReadOnlyDictionary<PlotKey, PlotSnapshot> plots,
        in CyclePosition position,
        DateTime nowUtc,
        NoticeLedger ledger,
        HashSet<string> keys,
        ImmutableArray<Notice>.Builder due)
    {
        var matches = search.Matches(plots, position, nowUtc);
        for (var index = 0; index < matches.Length; index++)
        {
            Offer(new Notice(NoticeKind.NewMatch, matches[index], search.Id, search.Name, position.Cycle, null), ledger, keys, due);
        }
    }

    private static void CollectList(
        SavedList list,
        IReadOnlyDictionary<PlotKey, PlotSnapshot> plots,
        in CyclePosition position,
        DateTime nowUtc,
        NoticeLedger ledger,
        HashSet<string> keys,
        ImmutableArray<Notice>.Builder due)
    {
        for (var index = 0; index < list.Plots.Length; index++)
        {
            var key = list.Plots[index];
            if (!plots.TryGetValue(key, out var plot))
            {
                continue;
            }

            var phaseEnd = plot.Sale?.Value.PhaseEndUtc;
            switch (PlotStatuses.Classify(plot, position))
            {
                case PlotStatus.EntryOpen:
                    Offer(new Notice(NoticeKind.EntryOpened, key, list.Id, list.Name, position.Cycle, phaseEnd), ledger, keys, due);
                    if (phaseEnd is { } end && end > nowUtc && end - nowUtc <= closingLead)
                    {
                        Offer(new Notice(NoticeKind.EntryClosing, key, list.Id, list.Name, position.Cycle, phaseEnd), ledger, keys, due);
                    }

                    break;
                case PlotStatus.Results:
                    Offer(new Notice(NoticeKind.ResultsAvailable, key, list.Id, list.Name, position.Cycle, phaseEnd), ledger, keys, due);
                    break;
            }
        }
    }

    private static void Offer(in Notice notice, NoticeLedger ledger, HashSet<string> keys, ImmutableArray<Notice>.Builder due)
    {
        if (ledger.Contains(notice) || !keys.Add(notice.DedupeKey))
        {
            return;
        }

        due.Add(notice);
    }
}
