using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Saved.Model;

// What a newly saved search or list, or a plot newly added to a list, already matches is recorded without being
// announced. A source or plot waits here until the listings for its world have loaded, and the set is saved with the
// library, so a reload in between does not announce everything it already knew.
internal sealed class NoticeBaseline
{
    private readonly HashSet<Guid> sources = [];
    private readonly HashSet<PlotKey> plots = [];

    public IReadOnlyCollection<Guid> Sources => sources;

    public IReadOnlyCollection<PlotKey> Plots => plots;

    public int Count => sources.Count + plots.Count;

    public bool AddSource(Guid id) => sources.Add(id);

    public bool AddPlot(PlotKey key) => plots.Add(key);

    public bool HasSource(Guid id) => sources.Contains(id);

    public bool HasPlot(PlotKey key) => plots.Contains(key);

    // A new match belongs to its search, so only that search's baseline holds it back; any other notice is about the plot.
    public bool Holds(in Notice notice) => sources.Contains(notice.SourceId) || (notice.Kind != NoticeKind.NewMatch && plots.Contains(notice.Plot));

    public void Forget(Guid id) => sources.Remove(id);

    public void Settle(ReadOnlySpan<Guid> readySources, ReadOnlySpan<PlotKey> readyPlots)
    {
        for (var index = 0; index < readySources.Length; index++)
        {
            sources.Remove(readySources[index]);
        }

        for (var index = 0; index < readyPlots.Length; index++)
        {
            plots.Remove(readyPlots[index]);
        }
    }

    public NoticeBaseline Copy()
    {
        var copy = new NoticeBaseline();
        copy.sources.UnionWith(sources);
        copy.plots.UnionWith(plots);
        return copy;
    }
}
