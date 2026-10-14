using System.Collections.Frozen;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Providers.Model;
using AutoHouseLottery.Core.Saved.Model;
using AutoHouseLottery.Core.Storage;

namespace AutoHouseLottery.Core.Saved;

// Saved searches, fixed lists and their notices, kept in saved.json. Ported from Aetherphone's watch store and reminder
// service: notices are worked out on a task from the latest listings and delivered on the framework thread, where the
// ledger records them so each one fires once, including when the plugin catches up after a login. A search or list
// saved a moment ago first records what it already matches without announcing it.
internal sealed class SavedService
{
    private const string FileName = "saved.json";
    private const long TicksPerMinute = TimeSpan.TicksPerMinute;

    private readonly QueuedFile file;
    private readonly Task<byte[]?> loading;
    private readonly NoticeLedger ledger = new();
    private readonly List<NoticeRecord> recent = [];
    private readonly HashSet<Guid> baselinePending = [];
    private Task<NoticeBatch>? evaluating;
    private EvaluationInputs lastInputs;
    private bool loaded;
    private bool dirty;

    public SavedService(string directory)
    {
        file = new QueuedFile(Path.Combine(directory, FileName), "saved searches and lists");
        loading = file.ReadAsync();
    }

    public SavedLibrary Library { get; } = new();

    public IReadOnlyList<NoticeRecord> Recent => recent;

    public int RecentRevision { get; private set; }

    public FrozenDictionary<Guid, ImmutableArray<PlotKey>> Matches { get; private set; } = FrozenDictionary<Guid, ImmutableArray<PlotKey>>.Empty;

    public void Update(DateTime nowUtc, DiscoverySnapshot snapshot, Action<IReadOnlyList<Notice>> announce)
    {
        if (!loaded)
        {
            if (!loading.IsCompleted)
            {
                return;
            }

            Load(loading.Result, nowUtc);
        }

        if (evaluating is { IsCompleted: true } finished)
        {
            evaluating = null;
            if (finished.IsCompletedSuccessfully)
            {
                Deliver(finished.Result, nowUtc, announce);
            }
            else
            {
                RunLog.Error(finished.Exception!.GetBaseException(), "Working out the saved search notices failed");
            }
        }

        var inputs = new EvaluationInputs(snapshot.PlotsRevision, Library.Revision, baselinePending.Count, nowUtc.Ticks / TicksPerMinute);
        if (evaluating is null && inputs != lastInputs && !Library.IsEmpty)
        {
            lastInputs = inputs;
            var library = Library.Snapshot();
            var ledgerCopy = ledger.Copy();
            var baseline = baselinePending.ToImmutableArray();
            evaluating = Task.Run(() => Evaluate(library, snapshot, ledgerCopy, baseline, nowUtc));
        }

        if (dirty)
        {
            dirty = false;
            Save();
        }
    }

    public SavedSearch SaveSearch(string name, in AreaScope area, in PlotFilter filter, DateTime nowUtc)
    {
        var search = Library.AddSearch(name, area, filter, nowUtc);
        baselinePending.Add(search.Id);
        dirty = true;
        RunLog.Info($"Saved the search \"{search.Name}\" for world {area.WorldId}.");
        return search;
    }

    public SavedList SaveList(string name, ReadOnlySpan<PlotKey> plots, DateTime nowUtc)
    {
        var list = Library.AddList(name, plots, nowUtc);
        baselinePending.Add(list.Id);
        dirty = true;
        RunLog.Info($"Saved the list \"{list.Name}\" with {list.Plots.Length} plots.");
        return list;
    }

    public void AddToList(Guid listId, PlotKey key)
    {
        if (Library.AddPlot(listId, key))
        {
            baselinePending.Add(listId);
            dirty = true;
        }
    }

    public void RemoveFromList(Guid listId, PlotKey key)
    {
        dirty |= Library.RemovePlot(listId, key);
    }

    public void Remove(Guid id)
    {
        if (Library.Remove(id))
        {
            baselinePending.Remove(id);
            dirty = true;
            RunLog.Info($"Removed a saved search or list ({id:N}).");
        }
    }

    public void ClearRecent()
    {
        recent.Clear();
        RecentRevision++;
        dirty = true;
    }

    public void Flush() => file.Flush();

    private void Load(byte[]? bytes, DateTime nowUtc)
    {
        loaded = true;
        if (bytes is null)
        {
            return;
        }

        var error = SavedCodec.TryRead(Encoding.UTF8.GetString(bytes), out var document);
        if (error != SavedReadError.None)
        {
            RunLog.Warning($"The saved searches and lists could not be read ({error}); starting with none.");
            file.BackUp(nowUtc);
            return;
        }

        Library.Restore(document!.Searches.AsSpan(), document.Lists.AsSpan());
        for (var index = 0; index < document.Delivered.Length; index++)
        {
            ledger.Restore(document.Delivered[index].Key, document.Delivered[index].Value);
        }

        recent.AddRange(document.Recent);
        RecentRevision++;
        if (ledger.Prune(nowUtc) > 0 || document.Skipped > 0)
        {
            dirty = true;
        }

        RunLog.Info($"Loaded {document.Searches.Length} saved searches and {document.Lists.Length} lists{(document.Skipped > 0 ? $"; {document.Skipped} damaged entries were skipped" : string.Empty)}.");
    }

    private void Deliver(NoticeBatch batch, DateTime nowUtc, Action<IReadOnlyList<Notice>> announce)
    {
        Matches = batch.Matches;
        for (var index = 0; index < batch.Silent.Length; index++)
        {
            dirty |= ledger.TryRecord(batch.Silent[index], nowUtc);
        }

        for (var index = 0; index < batch.BaselineDone.Length; index++)
        {
            baselinePending.Remove(batch.BaselineDone[index]);
        }

        var fresh = new List<Notice>();
        for (var index = 0; index < batch.Announce.Length; index++)
        {
            var notice = batch.Announce[index];
            if (!ledger.TryRecord(notice, nowUtc))
            {
                continue;
            }

            fresh.Add(notice);
            recent.Insert(0, new NoticeRecord(notice.Kind, notice.Plot, notice.SourceName, nowUtc, notice.PhaseEndUtc));
            RunLog.Info($"Saved plot notice: {notice.Kind} for {notice.Plot} in cycle {notice.Cycle} ({notice.SourceName}).");
        }

        if (fresh.Count == 0)
        {
            return;
        }

        if (recent.Count > SavedCodec.MaximumRecent)
        {
            recent.RemoveRange(SavedCodec.MaximumRecent, recent.Count - SavedCodec.MaximumRecent);
        }

        RecentRevision++;
        dirty = true;
        announce(fresh);
    }

    private void Save()
    {
        var library = Library.Snapshot();
        var delivered = new Dictionary<string, DateTime>(ledger.Entries);
        var records = recent.ToArray();
        file.Write(() => Encoding.UTF8.GetBytes(SavedCodec.Export(library, delivered, records)));
    }

    private static NoticeBatch Evaluate(SavedLibrarySnapshot library, DiscoverySnapshot snapshot, NoticeLedger ledger, ImmutableArray<Guid> baseline, DateTime nowUtc)
    {
        var plots = snapshot.Plots;
        var position = CycleResolver.Current(snapshot.Schedule, snapshot.Positions.AsSpan(), nowUtc);
        var matches = new Dictionary<Guid, ImmutableArray<PlotKey>>(library.Searches.Length);
        for (var index = 0; index < library.Searches.Length; index++)
        {
            matches[library.Searches[index].Id] = library.Searches[index].Matches(plots, position, nowUtc);
        }

        var ready = Ready(library, snapshot, baseline);
        var due = NoticeRules.Due(library, plots, position, nowUtc, ledger);
        var announce = ImmutableArray.CreateBuilder<Notice>();
        var silent = ImmutableArray.CreateBuilder<Notice>();
        for (var index = 0; index < due.Length; index++)
        {
            var notice = due[index];
            if (!baseline.Contains(notice.SourceId))
            {
                announce.Add(notice);
            }
            else if (ready.Contains(notice.SourceId))
            {
                silent.Add(notice);
            }
        }

        return new NoticeBatch(matches.ToFrozenDictionary(), announce.ToImmutable(), silent.ToImmutable(), ready);
    }

    // A new search or list can only take its starting point once the listings for its worlds are loaded; until then its
    // notices are held back rather than recorded.
    private static ImmutableArray<Guid> Ready(in SavedLibrarySnapshot library, DiscoverySnapshot snapshot, ImmutableArray<Guid> baseline)
    {
        var ready = ImmutableArray.CreateBuilder<Guid>();
        for (var index = 0; index < library.Searches.Length; index++)
        {
            var search = library.Searches[index];
            if (baseline.Contains(search.Id) && snapshot.StatusOf(search.Area.WorldId).HasData)
            {
                ready.Add(search.Id);
            }
        }

        for (var listIndex = 0; listIndex < library.Lists.Length; listIndex++)
        {
            var list = library.Lists[listIndex];
            if (!baseline.Contains(list.Id))
            {
                continue;
            }

            var loadedWorlds = true;
            for (var plotIndex = 0; plotIndex < list.Plots.Length && loadedWorlds; plotIndex++)
            {
                loadedWorlds = snapshot.StatusOf(list.Plots[plotIndex].WorldId).HasData;
            }

            if (loadedWorlds)
            {
                ready.Add(list.Id);
            }
        }

        return ready.ToImmutable();
    }

    private readonly record struct EvaluationInputs(int PlotsRevision, int LibraryRevision, int Pending, long Minute);

    private sealed record NoticeBatch(
        FrozenDictionary<Guid, ImmutableArray<PlotKey>> Matches,
        ImmutableArray<Notice> Announce,
        ImmutableArray<Notice> Silent,
        ImmutableArray<Guid> BaselineDone);
}
