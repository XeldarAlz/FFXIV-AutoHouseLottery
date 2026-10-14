using System.Collections.Frozen;
using System.Collections.Immutable;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Providers.Model;

// An immutable copy of everything known, handed from the background merge to the page and the planners.
internal sealed class DiscoverySnapshot
{
    public static readonly DiscoverySnapshot Empty = new()
    {
        Revision = 0,
        PlotsRevision = 0,
        Plots = FrozenDictionary<PlotKey, PlotSnapshot>.Empty,
        Positions = [],
        Worlds = FrozenDictionary<uint, FeedWorldStatus>.Empty,
        Coverage = FrozenDictionary<(uint World, ushort District), CoverageSnapshot>.Empty,
        WorldList = [],
        Schedule = LotteryCalendar.Global,
    };

    public required int Revision { get; init; }

    public required int PlotsRevision { get; init; }

    public required IReadOnlyDictionary<PlotKey, PlotSnapshot> Plots { get; init; }

    public required ImmutableArray<CyclePosition> Positions { get; init; }

    public required FrozenDictionary<uint, FeedWorldStatus> Worlds { get; init; }

    public required FrozenDictionary<(uint World, ushort District), CoverageSnapshot> Coverage { get; init; }

    public required ImmutableArray<FeedWorld> WorldList { get; init; }

    public required LotterySchedule Schedule { get; init; }

    public FeedWorldStatus StatusOf(uint worldId) => Worlds.TryGetValue(worldId, out var status) ? status : default;
}
