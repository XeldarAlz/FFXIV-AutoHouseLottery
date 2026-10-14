using System.Collections.Frozen;
using System.Collections.Immutable;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Find.Model;

// One immutable answer to a query. Add all matching adds exactly MatchingKeys, the rows the player is looking at, so the
// count on its button and what it adds can never disagree.
internal sealed class FindResults
{
    public static readonly FindResults Empty = new()
    {
        Query = default,
        Rows = [],
        MatchingKeys = [],
        Matching = FrozenSet<PlotKey>.Empty,
        WardMatches = FrozenDictionary<ushort, ImmutableArray<int>>.Empty,
        ReportedVacant = 0,
        EntryOpen = 0,
    };

    public required FindQuery Query { get; init; }

    public required ImmutableArray<ResultRow> Rows { get; init; }

    public required ImmutableArray<PlotKey> MatchingKeys { get; init; }

    public required FrozenSet<PlotKey> Matching { get; init; }

    public required FrozenDictionary<ushort, ImmutableArray<int>> WardMatches { get; init; }

    public required int ReportedVacant { get; init; }

    public required int EntryOpen { get; init; }

    public int Count => Rows.Length;

    public int MatchesInWard(ushort districtId, int ward)
    {
        return WardMatches.TryGetValue(districtId, out var counts) && ward >= 1 && ward <= counts.Length ? counts[ward - 1] : 0;
    }
}
