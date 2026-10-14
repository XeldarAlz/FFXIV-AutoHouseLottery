using System.Collections.Immutable;
using System.Runtime.InteropServices;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Selection.Model;

internal static class BestMatchRanking
{
    public static ImmutableArray<RankedPlot> Rank(ReadOnlySpan<PlotKey> keys, IReadOnlyDictionary<PlotKey, PlotSnapshot> plots, CycleKey cycle)
    {
        var ranked = new RankedPlot[keys.Length];
        for (var index = 0; index < keys.Length; index++)
        {
            var key = keys[index];
            plots.TryGetValue(key, out var snapshot);
            ranked[index] = new RankedPlot(key, snapshot?.EntryCountIn(cycle), snapshot?.Price?.Value);
        }

        Array.Sort(ranked, Compare);
        return ImmutableCollectionsMarshal.AsImmutableArray(ranked);
    }

    public static int Compare(RankedPlot left, RankedPlot right)
    {
        var comparison = CompareKnownFirst(left.ReportedEntries, right.ReportedEntries);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = CompareKnownFirst(left.Deposit, right.Deposit);
        return comparison != 0 ? comparison : left.Key.CompareTo(right.Key);
    }

    private static int CompareKnownFirst<T>(T? left, T? right) where T : struct, IComparable<T>
    {
        if (left is { } knownLeft)
        {
            return right is { } knownRight ? knownLeft.CompareTo(knownRight) : -1;
        }

        return right is null ? 0 : 1;
    }
}
