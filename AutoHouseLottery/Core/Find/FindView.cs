using System.Collections.Immutable;
using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Selection.Model;

namespace AutoHouseLottery.Core.Find;

// Everything the Find a home page draws, computed together on a task from one snapshot so the map, the list, the
// shortlist drawer and the preview always describe the same data.
internal sealed class FindView
{
    public static readonly FindView Empty = new()
    {
        Results = FindResults.Empty,
        Plots = Providers.Model.DiscoverySnapshot.Empty.Plots,
        Position = default,
        Shortlist = [],
        ShortlistRevision = -1,
        QueryRevision = -1,
        Ranking = [],
        Preview = null,
        UnavailableCount = 0,
    };

    public required FindResults Results { get; init; }

    public required IReadOnlyDictionary<PlotKey, PlotSnapshot> Plots { get; init; }

    public required CyclePosition Position { get; init; }

    public required ImmutableArray<ShortlistRow> Shortlist { get; init; }

    public required int ShortlistRevision { get; init; }

    public required int QueryRevision { get; init; }

    public required ImmutableArray<RankedPlot> Ranking { get; init; }

    public required RunPreview? Preview { get; init; }

    public required int UnavailableCount { get; init; }

    public PlotSnapshot? Plot(PlotKey key) => Plots.TryGetValue(key, out var plot) ? plot : null;

    public PlotStatus? StatusOf(PlotKey key) => Plots.TryGetValue(key, out var plot) ? PlotStatuses.Classify(plot, Position) : null;
}
