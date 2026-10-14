using System.Collections.Immutable;
using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Saved.Model;

internal sealed class SavedLibrary
{
    public const int MaximumNameLength = 48;

    private readonly List<SavedSearch> searches = [];
    private readonly List<SavedList> lists = [];

    public int Revision { get; private set; }

    public IReadOnlyList<SavedSearch> Searches => searches;

    public IReadOnlyList<SavedList> Lists => lists;

    public bool IsEmpty => searches.Count == 0 && lists.Count == 0;

    public SavedSearch AddSearch(string name, in AreaScope area, in PlotFilter filter, DateTime nowUtc)
    {
        var search = new SavedSearch(Guid.NewGuid(), Clean(name), area, filter, nowUtc);
        searches.Add(search);
        Revision++;
        return search;
    }

    public SavedList AddList(string name, ReadOnlySpan<PlotKey> plots, DateTime nowUtc)
    {
        var list = new SavedList(Guid.NewGuid(), Clean(name), Distinct(plots), nowUtc);
        lists.Add(list);
        Revision++;
        return list;
    }

    // The file loads in the background; anything saved before it arrived is kept after what was already on disk.
    public void MergeLoaded(ReadOnlySpan<SavedSearch> savedSearches, ReadOnlySpan<SavedList> savedLists)
    {
        var added = searches.ToArray();
        searches.Clear();
        searches.AddRange(savedSearches);
        for (var index = 0; index < added.Length; index++)
        {
            if (!HasSearch(added[index].Id))
            {
                searches.Add(added[index]);
            }
        }

        var addedLists = lists.ToArray();
        lists.Clear();
        lists.AddRange(savedLists);
        for (var index = 0; index < addedLists.Length; index++)
        {
            if (IndexOfList(addedLists[index].Id) < 0)
            {
                lists.Add(addedLists[index]);
            }
        }

        Revision++;
    }

    public bool Remove(Guid id)
    {
        var removed = searches.RemoveAll(search => search.Id == id) + lists.RemoveAll(list => list.Id == id);
        if (removed == 0)
        {
            return false;
        }

        Revision++;
        return true;
    }

    public bool RemovePlot(Guid listId, PlotKey key)
    {
        var index = IndexOfList(listId);
        if (index < 0 || !lists[index].Contains(key))
        {
            return false;
        }

        lists[index] = lists[index] with { Plots = lists[index].Plots.Remove(key) };
        Revision++;
        return true;
    }

    public bool AddPlot(Guid listId, PlotKey key)
    {
        var index = IndexOfList(listId);
        if (index < 0 || lists[index].Contains(key))
        {
            return false;
        }

        lists[index] = lists[index] with { Plots = lists[index].Plots.Add(key) };
        Revision++;
        return true;
    }

    public bool IsSaved(PlotKey key)
    {
        for (var index = 0; index < lists.Count; index++)
        {
            if (lists[index].Contains(key))
            {
                return true;
            }
        }

        return false;
    }

    public void CollectWorlds(List<uint> worlds)
    {
        for (var index = 0; index < searches.Count; index++)
        {
            AddWorld(worlds, searches[index].Area.WorldId);
        }

        for (var listIndex = 0; listIndex < lists.Count; listIndex++)
        {
            var plots = lists[listIndex].Plots;
            for (var plotIndex = 0; plotIndex < plots.Length; plotIndex++)
            {
                AddWorld(worlds, plots[plotIndex].WorldId);
            }
        }
    }

    public SavedLibrarySnapshot Snapshot() => new(Revision, [.. searches], [.. lists]);

    private bool HasSearch(Guid id)
    {
        for (var index = 0; index < searches.Count; index++)
        {
            if (searches[index].Id == id)
            {
                return true;
            }
        }

        return false;
    }

    private int IndexOfList(Guid listId)
    {
        for (var index = 0; index < lists.Count; index++)
        {
            if (lists[index].Id == listId)
            {
                return index;
            }
        }

        return -1;
    }

    private static void AddWorld(List<uint> worlds, uint worldId)
    {
        if (worldId != 0 && !worlds.Contains(worldId))
        {
            worlds.Add(worldId);
        }
    }

    private static ImmutableArray<PlotKey> Distinct(ReadOnlySpan<PlotKey> plots)
    {
        var seen = new HashSet<PlotKey>(plots.Length);
        var distinct = ImmutableArray.CreateBuilder<PlotKey>(plots.Length);
        for (var index = 0; index < plots.Length; index++)
        {
            if (seen.Add(plots[index]))
            {
                distinct.Add(plots[index]);
            }
        }

        return distinct.ToImmutable();
    }

    private static string Clean(string name)
    {
        var trimmed = name.Trim();
        return trimmed.Length > MaximumNameLength ? trimmed[..MaximumNameLength] : trimmed;
    }
}
