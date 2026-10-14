using System.Collections.Immutable;
using System.Runtime.InteropServices;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Selection.Model;

internal static class BestMatchRanking
{
    private const int OpenForEntryTier = 0;
    private const int UnconfirmedTier = 1;
    private const int UnavailableTier = 2;

    public static ImmutableArray<RankedPlot> Rank(ReadOnlySpan<PlotKey> keys, IReadOnlyDictionary<PlotKey, PlotSnapshot> plots, in CyclePosition position)
    {
        var ranked = new RankedPlot[keys.Length];
        for (var index = 0; index < keys.Length; index++)
        {
            var key = keys[index];
            plots.TryGetValue(key, out var snapshot);
            ranked[index] = new RankedPlot(key, snapshot?.EntryCountIn(position.Cycle), snapshot?.Price?.Value)
            {
                Availability = snapshot?.AvailabilityAt(position) ?? PlotAvailability.Unknown,
            };
        }

        Array.Sort(ranked, Compare);
        return ImmutableCollectionsMarshal.AsImmutableArray(ranked);
    }

    public static ImmutableArray<RankedPlot> Rank(ReadOnlySpan<PlotKey> keys, IReadOnlyDictionary<PlotKey, PlotSnapshot> plots, CycleKey cycle)
    {
        return Rank(keys, plots, new CyclePosition(cycle, LotteryPhase.Entry, default, default, 0, CycleEvidence.Estimated));
    }

    public static int Compare(RankedPlot left, RankedPlot right)
    {
        var comparison = Tier(left.Availability).CompareTo(Tier(right.Availability));
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = CompareKnownFirst(left.ReportedEntries, right.ReportedEntries);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = CompareKnownFirst(left.Deposit, right.Deposit);
        return comparison != 0 ? comparison : left.Key.CompareTo(right.Key);
    }

    private static int Tier(PlotAvailability availability) => availability switch
    {
        PlotAvailability.LotteryEntry => OpenForEntryTier,
        PlotAvailability.Unknown or PlotAvailability.NotReported => UnconfirmedTier,
        _ => UnavailableTier,
    };

    private static int CompareKnownFirst<T>(T? left, T? right) where T : struct, IComparable<T>
    {
        if (left is { } knownLeft)
        {
            return right is { } knownRight ? knownLeft.CompareTo(knownRight) : -1;
        }

        return right is null ? 0 : 1;
    }
}
