using System.Collections.Immutable;
using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Saved.Model;

// A search keeps its definition, not its results: populating a shortlist from it asks the current listings again.
internal sealed record SavedSearch(Guid Id, string Name, AreaScope Area, PlotFilter Filter, DateTime CreatedUtc)
{
    public ImmutableArray<PlotKey> Matches(IReadOnlyDictionary<PlotKey, PlotSnapshot> plots, in CyclePosition position, DateTime nowUtc)
    {
        var query = new FindQuery(Area, Filter, ResultSort.Address, 0, position, nowUtc);
        return FindResultsBuilder.Build(plots, query).MatchingKeys;
    }
}
