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
// service: the notice plan is worked out on a task from the latest listings and delivered on the framework thread, where
// the ledger records it so each notice fires once, including when the plugin catches up after a login. Nothing is written
// over saved.json until it has been read, and what was saved meanwhile is merged with it.
internal sealed class SavedService
{
    private const string FileName = "saved.json";

    private static readonly TimeSpan pruneInterval = TimeSpan.FromHours(1);

    private readonly QueuedFile file;
    private readonly Task<FileRead> loading;
    private readonly NoticeLedger ledger = new();
    private readonly NoticeBaseline baseline = new();
    private readonly List<NoticeRecord> recent = [];
    private Task<NoticePlan>? planning;
    private PlanInputs lastInputs;
    private bool loaded;
    private bool dirty;
    private DateTime nextPruneUtc;

    public SavedService(string directory)
    {
        file = new QueuedFile(Path.Combine(directory, FileName), "saved searches and lists", static (exception, message) => RunLog.Warning(exception, message));
        loading = file.ReadWhenReadableAsync();
    }

    public SavedLibrary Library { get; } = new();

    public IReadOnlyList<NoticeRecord> Recent => recent;

    public int RecentRevision { get; private set; }

    public int MatchesRevision { get; private set; }

    public FrozenDictionary<Guid, ImmutableArray<PlotKey>> Matches { get; private set; } = FrozenDictionary<Guid, ImmutableArray<PlotKey>>.Empty;

    public void Update(DateTime nowUtc, DiscoverySnapshot snapshot, Action<IReadOnlyList<Notice>> announce)
    {
        if (!loaded)
        {
            if (!loading.IsCompleted || loading.Result.Status == FileReadStatus.Failed)
            {
                return;
            }

            Load(loading.Result.Bytes, nowUtc);
        }

        if (planning is { IsCompleted: true } finished)
        {
            planning = null;
            if (finished.IsCompletedSuccessfully)
            {
                Deliver(finished.Result, nowUtc, announce);
            }
            else
            {
                RunLog.Error(finished.Exception!.GetBaseException(), "Working out the saved search notices failed");
            }
        }

        var inputs = new PlanInputs(snapshot.Revision, Library.Revision, baseline.Count, nowUtc.Ticks / TimeSpan.TicksPerMinute);
        if (planning is null && inputs != lastInputs && !Library.IsEmpty)
        {
            lastInputs = inputs;
            var library = Library.Snapshot();
            var ledgerCopy = ledger.Copy();
            var baselineCopy = baseline.Copy();
            var loadedWorlds = LoadedWorlds(snapshot);
            planning = Task.Run(() => Plan(library, snapshot, ledgerCopy, baselineCopy, loadedWorlds, nowUtc));
        }

        SaveIfDirty();
    }

    public SavedSearch SaveSearch(string name, in AreaScope area, in PlotFilter filter, DateTime nowUtc)
    {
        var search = Library.AddSearch(name, area, filter, nowUtc);
        baseline.AddSource(search.Id);
        dirty = true;
        RunLog.Info($"Saved the search \"{search.Name}\" for world {area.WorldId}.");
        return search;
    }

    public SavedList SaveList(string name, ReadOnlySpan<PlotKey> plots, DateTime nowUtc)
    {
        var list = Library.AddList(name, plots, nowUtc);
        baseline.AddSource(list.Id);
        dirty = true;
        RunLog.Info($"Saved the list \"{list.Name}\" with {list.Plots.Length} plots.");
        return list;
    }

    public void AddToList(Guid listId, PlotKey key)
    {
        if (Library.AddPlot(listId, key))
        {
            baseline.AddPlot(key);
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
            baseline.Forget(id);
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

    public void Close()
    {
        SaveIfDirty();
        file.Close();
    }

    private void SaveIfDirty()
    {
        if (!loaded || !dirty)
        {
            return;
        }

        dirty = false;
        var library = Library.Snapshot();
        var delivered = new Dictionary<string, DateTime>(ledger.Entries);
        var records = recent.ToArray();
        var pending = baseline.Copy();
        file.Write(() => Encoding.UTF8.GetBytes(SavedCodec.Export(library, delivered, records, pending)));
    }

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
            RunLog.Warning($"The saved searches and lists could not be read ({error}); the file is kept under a dated name and the page starts empty.");
            file.BackUp(nowUtc);
            dirty = true;
            return;
        }

        Library.MergeLoaded(document!.Searches.AsSpan(), document.Lists.AsSpan());
        for (var index = 0; index < document.Delivered.Length; index++)
        {
            ledger.Restore(document.Delivered[index].Key, document.Delivered[index].Value);
        }

        for (var index = 0; index < document.PendingSources.Length; index++)
        {
            baseline.AddSource(document.PendingSources[index]);
        }

        for (var index = 0; index < document.PendingPlots.Length; index++)
        {
            baseline.AddPlot(document.PendingPlots[index]);
        }

        recent.InsertRange(0, document.Recent);
        RecentRevision++;
        dirty |= ledger.Prune(nowUtc) > 0 || document.Skipped > 0;
        RunLog.Info($"Loaded {document.Searches.Length} saved searches and {document.Lists.Length} lists{(document.Skipped > 0 ? $"; {document.Skipped} damaged entries were skipped" : string.Empty)}.");
    }

    private void Deliver(NoticePlan plan, DateTime nowUtc, Action<IReadOnlyList<Notice>> announce)
    {
        Matches = plan.Matches;
        MatchesRevision++;
        if (nowUtc >= nextPruneUtc)
        {
            nextPruneUtc = nowUtc + pruneInterval;
            dirty |= ledger.Prune(nowUtc) > 0;
        }
        for (var index = 0; index < plan.Silent.Length; index++)
        {
            dirty |= ledger.TryRecord(plan.Silent[index], nowUtc);
        }

        if (!plan.ReadySources.IsEmpty || !plan.ReadyPlots.IsEmpty)
        {
            baseline.Settle(plan.ReadySources.AsSpan(), plan.ReadyPlots.AsSpan());
            dirty = true;
        }

        var fresh = new List<Notice>();
        for (var index = 0; index < plan.Announce.Length; index++)
        {
            var notice = plan.Announce[index];
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

    private static NoticePlan Plan(SavedLibrarySnapshot library, DiscoverySnapshot snapshot, NoticeLedger ledger, NoticeBaseline baseline, FrozenSet<uint> loadedWorlds, DateTime nowUtc)
    {
        var position = CycleResolver.Current(snapshot.Schedule, snapshot.Positions.AsSpan(), nowUtc);
        return NoticePlanner.Plan(library, snapshot.Plots, position, nowUtc, ledger, baseline, loadedWorlds);
    }

    private static FrozenSet<uint> LoadedWorlds(DiscoverySnapshot snapshot)
    {
        var worlds = new List<uint>(snapshot.Worlds.Count);
        foreach (var pair in snapshot.Worlds)
        {
            if (pair.Value.HasData)
            {
                worlds.Add(pair.Key);
            }
        }

        return worlds.ToFrozenSet();
    }

    private readonly record struct PlanInputs(int SnapshotRevision, int LibraryRevision, int Pending, long Minute);
}
