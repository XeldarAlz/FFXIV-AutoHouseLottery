using System.Collections.Immutable;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Saved.Model;

// A fixed list keeps the exact plots it was saved with, whatever the listings later say about them.
internal sealed record SavedList(Guid Id, string Name, ImmutableArray<PlotKey> Plots, DateTime CreatedUtc)
{
    public bool Contains(PlotKey key) => Plots.Contains(key);
}
