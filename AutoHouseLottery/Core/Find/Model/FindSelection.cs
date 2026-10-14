using System.Collections.Immutable;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Selection.Model;

namespace AutoHouseLottery.Core.Find.Model;

// The query the page shows and the one shortlist the map, the list and the drawers share. Changing the query never
// touches the shortlist; only the shortlist's own operations do, so a filter change cannot drop a plot the player chose.
internal sealed class FindSelection
{
    public FindSelection(Shortlist shortlist, in AreaScope area, in PlotFilter filter, ResultSort sort, uint preferredDistricts)
    {
        Shortlist = shortlist;
        Area = area;
        Filter = filter;
        Sort = sort;
        PreferredDistricts = preferredDistricts;
    }

    public Shortlist Shortlist { get; private set; }

    public AreaScope Area { get; private set; }

    public PlotFilter Filter { get; private set; }

    public ResultSort Sort { get; private set; }

    public uint PreferredDistricts { get; private set; }

    public int QueryRevision { get; private set; }

    public void WorldChanged() => QueryRevision++;

    public void SetArea(in AreaScope area)
    {
        Area = area;
        QueryRevision++;
    }

    public void SetFilter(in PlotFilter filter)
    {
        Filter = filter;
        QueryRevision++;
    }

    public void SetSort(ResultSort sort)
    {
        Sort = sort;
        QueryRevision++;
    }

    public void SetPreferred(uint preferredDistricts)
    {
        PreferredDistricts = preferredDistricts;
        QueryRevision++;
    }

    public bool Toggle(PlotKey key)
    {
        if (Shortlist.Remove(key))
        {
            return false;
        }

        Shortlist.Add(key);
        return true;
    }

    public int AddAll(ImmutableArray<PlotKey> keys) => Shortlist.AddRange(keys.AsSpan());

    public bool Remove(PlotKey key) => Shortlist.Remove(key);

    public int RemoveUnavailable(ImmutableArray<ShortlistRow> rows)
    {
        var unavailable = ShortlistRules.Unavailable(rows);
        var removed = 0;
        for (var index = 0; index < unavailable.Length; index++)
        {
            if (Shortlist.Remove(unavailable[index]))
            {
                removed++;
            }
        }

        return removed;
    }

    // The saved shortlist arrives after the page may already have been used: an untouched shortlist is replaced, so it
    // keeps its saved identity, and one already in use gains the saved plots.
    public bool TakeLoaded(Shortlist loaded)
    {
        if (Shortlist.Count == 0)
        {
            Shortlist = loaded;
            return true;
        }

        Shortlist.AddRange([.. loaded.Plots]);
        return false;
    }
}
