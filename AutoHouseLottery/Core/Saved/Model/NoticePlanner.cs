using System.Collections.Frozen;
using System.Collections.Immutable;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Saved.Model;

// One pass over the saved library: what each search matches now, which notices are new, and which of them only settle a
// baseline. A baseline settles once the listings of every world it covers have loaded; until then its notices are held
// back rather than recorded.
internal static class NoticePlanner
{
    public static NoticePlan Plan(
        in SavedLibrarySnapshot library,
        IReadOnlyDictionary<PlotKey, PlotSnapshot> plots,
        in CyclePosition position,
        DateTime nowUtc,
        NoticeLedger ledger,
        NoticeBaseline baseline,
        IReadOnlySet<uint> loadedWorlds)
    {
        var matches = new Dictionary<Guid, ImmutableArray<PlotKey>>(library.Searches.Length);
        for (var index = 0; index < library.Searches.Length; index++)
        {
            matches[library.Searches[index].Id] = library.Searches[index].Matches(plots, position, nowUtc);
        }

        var readySources = ReadySources(library, baseline, loadedWorlds);
        var readyPlots = ImmutableArray.CreateBuilder<PlotKey>();
        foreach (var plot in baseline.Plots)
        {
            if (loadedWorlds.Contains(plot.WorldId))
            {
                readyPlots.Add(plot);
            }
        }

        var due = NoticeRules.Due(library, plots, position, nowUtc, ledger);
        var announce = ImmutableArray.CreateBuilder<Notice>();
        var silent = ImmutableArray.CreateBuilder<Notice>();
        for (var index = 0; index < due.Length; index++)
        {
            var notice = due[index];
            if (!baseline.Holds(notice))
            {
                announce.Add(notice);
                continue;
            }

            if (readySources.Contains(notice.SourceId) || (baseline.HasPlot(notice.Plot) && readyPlots.Contains(notice.Plot)))
            {
                silent.Add(notice);
            }
        }

        return new NoticePlan(matches.ToFrozenDictionary(), announce.ToImmutable(), silent.ToImmutable(), readySources, readyPlots.ToImmutable());
    }

    private static ImmutableArray<Guid> ReadySources(in SavedLibrarySnapshot library, NoticeBaseline baseline, IReadOnlySet<uint> loadedWorlds)
    {
        var ready = ImmutableArray.CreateBuilder<Guid>();
        for (var index = 0; index < library.Searches.Length; index++)
        {
            var search = library.Searches[index];
            if (baseline.HasSource(search.Id) && loadedWorlds.Contains(search.Area.WorldId))
            {
                ready.Add(search.Id);
            }
        }

        for (var listIndex = 0; listIndex < library.Lists.Length; listIndex++)
        {
            var list = library.Lists[listIndex];
            if (!baseline.HasSource(list.Id))
            {
                continue;
            }

            var loaded = true;
            for (var plotIndex = 0; plotIndex < list.Plots.Length && loaded; plotIndex++)
            {
                loaded = loadedWorlds.Contains(list.Plots[plotIndex].WorldId);
            }

            if (loaded)
            {
                ready.Add(list.Id);
            }
        }

        return ready.ToImmutable();
    }
}
