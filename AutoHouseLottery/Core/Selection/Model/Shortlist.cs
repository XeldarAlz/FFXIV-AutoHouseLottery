using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Selection.Model;

internal sealed class Shortlist
{
    private readonly List<PlotKey> plots = [];
    private readonly HashSet<PlotKey> members = [];

    public Shortlist(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    public Guid Id { get; }

    public string Name { get; }

    public int Revision { get; private set; }

    public int Count => plots.Count;

    public IReadOnlyList<PlotKey> Plots => plots;

    public bool Contains(PlotKey key) => members.Contains(key);

    public bool Add(PlotKey key)
    {
        if (!members.Add(key))
        {
            return false;
        }

        plots.Add(key);
        Revision++;
        return true;
    }

    public int AddRange(ReadOnlySpan<PlotKey> keys)
    {
        var added = 0;
        for (var index = 0; index < keys.Length; index++)
        {
            if (!members.Add(keys[index]))
            {
                continue;
            }

            plots.Add(keys[index]);
            added++;
        }

        if (added > 0)
        {
            Revision++;
        }

        return added;
    }

    public bool Remove(PlotKey key)
    {
        if (!members.Remove(key))
        {
            return false;
        }

        plots.Remove(key);
        Revision++;
        return true;
    }

    public void Clear()
    {
        if (plots.Count == 0)
        {
            return;
        }

        plots.Clear();
        members.Clear();
        Revision++;
    }

    public ShortlistSnapshot Freeze() => new(Id, Revision, [.. plots]);
}
