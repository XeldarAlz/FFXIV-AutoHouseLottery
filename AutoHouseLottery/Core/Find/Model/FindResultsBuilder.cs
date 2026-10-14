using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Find.Model;

internal static class FindResultsBuilder
{
    public static FindResults Build(IReadOnlyDictionary<PlotKey, PlotSnapshot> plots, in FindQuery query)
    {
        var rows = new List<ResultRow>();
        var wardMatches = new Dictionary<ushort, int[]>();
        var reportedVacant = 0;
        var entryOpen = 0;
        foreach (var plot in plots.Values)
        {
            if (!query.Area.Contains(plot.Key))
            {
                continue;
            }

            var row = PlotStatuses.Row(plot, query.Position);
            if (PlotStatuses.IsReportedVacant(row.Status))
            {
                reportedVacant++;
            }

            if (row.Status == PlotStatus.EntryOpen)
            {
                entryOpen++;
            }

            if (!query.Filter.Matches(row, query.NowUtc))
            {
                continue;
            }

            rows.Add(row);
            CountWard(wardMatches, row.Key);
        }

        rows.Sort(ResultOrdering.For(query.Sort, query.PreferredDistricts));
        var keys = new PlotKey[rows.Count];
        for (var index = 0; index < keys.Length; index++)
        {
            keys[index] = rows[index].Key;
        }

        var wards = new Dictionary<ushort, ImmutableArray<int>>(wardMatches.Count);
        foreach (var pair in wardMatches)
        {
            wards[pair.Key] = [.. pair.Value];
        }

        return new FindResults
        {
            Query = query,
            Rows = [.. rows],
            MatchingKeys = ImmutableCollectionsMarshal.AsImmutableArray(keys),
            Matching = keys.ToFrozenSet(),
            WardMatches = wards.ToFrozenDictionary(),
            ReportedVacant = reportedVacant,
            EntryOpen = entryOpen,
        };
    }

    private static void CountWard(Dictionary<ushort, int[]> wardMatches, in PlotKey key)
    {
        if (!wardMatches.TryGetValue(key.DistrictId, out var counts))
        {
            HousingDistricts.TryGet(key.DistrictId, out var district);
            counts = new int[district.WardCount];
            wardMatches[key.DistrictId] = counts;
        }

        counts[key.Ward - 1]++;
    }
}
