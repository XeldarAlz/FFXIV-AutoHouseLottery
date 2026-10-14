using System.Collections.Immutable;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Find.Model;

internal static class ShortlistRules
{
    // Only a plot known to be out of reach goes: an entry period that already moved on, a plot listed for another mode
    // or no longer listed. A plot nobody has reported on yet stays, since unknown is not unavailable.
    public static bool IsUnavailable(PlotStatus? status)
    {
        return status is PlotStatus.Results or PlotStatus.NextCycle or PlotStatus.ForSale or PlotStatus.Occupied or PlotStatus.NotReported;
    }

    public static ImmutableArray<ShortlistRow> Rows(IReadOnlyList<PlotKey> shortlist, IReadOnlyDictionary<PlotKey, PlotSnapshot> plots, in CyclePosition position)
    {
        var rows = ImmutableArray.CreateBuilder<ShortlistRow>(shortlist.Count);
        for (var index = 0; index < shortlist.Count; index++)
        {
            var key = shortlist[index];
            if (!plots.TryGetValue(key, out var plot))
            {
                rows.Add(new ShortlistRow(key, null, null, null, null));
                continue;
            }

            rows.Add(new ShortlistRow(key, PlotStatuses.Classify(plot, position), plot.Size?.Value, plot.Price?.Value, plot.EntryCountIn(position.Cycle)));
        }

        return rows.MoveToImmutable();
    }

    public static ImmutableArray<PlotKey> Unavailable(ImmutableArray<ShortlistRow> rows)
    {
        var unavailable = ImmutableArray.CreateBuilder<PlotKey>();
        for (var index = 0; index < rows.Length; index++)
        {
            if (IsUnavailable(rows[index].Status))
            {
                unavailable.Add(rows[index].Key);
            }
        }

        return unavailable.ToImmutable();
    }
}
