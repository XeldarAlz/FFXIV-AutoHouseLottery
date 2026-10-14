using System.IO;
using System.Threading.Tasks;
using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Integration.Model;
using AutoHouseLottery.Core.Persistence;
using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Providers;

namespace AutoHouseLottery.Core.Integration;

// The action journal for the plugin's lifetime, and the one place game readings become domain facts. The plot store,
// the lottery calendar and the cycle position belong to the housing feed service; readings made in game are routed
// through it, so the listings, the placard and the ward scans share one store and one calendar. Readings arrive on the
// framework thread; disk work is sent off it.
internal sealed class HousingContext : IDisposable
{
    public const string JournalFileName = "journal.jsonl";

    // This plugin runs on the global client; the Chinese and Korean clients are separate Dalamud builds.
    public const GameRegion Region = GameRegion.Global;

    private static readonly JournalSnapshot emptyJournal = new([], [], []);

    private readonly Configuration configuration;
    private readonly HousingFeedService feed;
    private LotterySnapshot? lastReading;

    public HousingContext(Configuration configuration, string directory, HousingFeedService feed)
    {
        this.configuration = configuration;
        this.feed = feed;
        Directory.CreateDirectory(directory);
        var opened = ActionJournal.Open(Path.Combine(directory, JournalFileName), DateTime.UtcNow);
        Journal = opened.Journal;
        JournalStatus = opened.Status;
        JournalBackupPath = opened.BackupPath;
        RecoveredAttempts = opened.RecoveredAttempts;
        RunLog.Info($"Journal: {opened.Status}{(opened.RecoveredAttempts > 0 ? $", {opened.RecoveredAttempts} uncertain attempt(s) set aside for reconciliation" : string.Empty)}"
                  + $"{(opened.TruncatedTail ? ", a torn last line was dropped" : string.Empty)}{(opened.BackupPath is { } backup ? $", backup at {backup}" : string.Empty)}.");
    }

    public ActionJournal? Journal { get; }

    public JournalOpenStatus JournalStatus { get; }

    public string? JournalBackupPath { get; }

    public int RecoveredAttempts { get; }

    public bool JournalWritable => Journal is { Faulted: false };

    public JournalSnapshot JournalSnapshot => Journal?.Snapshot() ?? emptyJournal;

    public IReadOnlyDictionary<PlotKey, PlotSnapshot> Plots => feed.Snapshot.Plots;

    public LotterySchedule Schedule => feed.Snapshot.Schedule;

    public void Dispose() => Journal?.Dispose();

    // The single cycle position: a phase the game or the listings reported wins while it lasts, otherwise the calendar
    // estimate stands.
    public CyclePosition CurrentPosition(DateTime nowUtc)
    {
        var snapshot = feed.Snapshot;
        return CycleResolver.Current(snapshot.Schedule, snapshot.Positions.AsSpan(), nowUtc);
    }

    // The character's latest lottery reading from any Timers pass, kept so its profile can use it between passes.
    public LotterySnapshot? LastReading(ulong contentId) => lastReading is { } reading && reading.ContentId == contentId ? reading : null;

    // The placard's phase end may anchor the calendar, and its agreed fields become a placard observation. Returns the
    // cycle position the placard proved, null when it proved none.
    public CyclePosition? ObservePlacard(PlacardReading reading, uint worldId)
    {
        CyclePosition? proved = null;
        if (PlacardObservations.PhaseOf(reading.Sale, reading.ReadAtUtc) is { } phase)
        {
            if (feed.TryObservePhase(phase.Phase, phase.EndsAtUtc, out var position))
            {
                proved = position;
            }

            RunLog.Info($"Lottery calendar: the placard reports the {phase.Phase} phase ending {phase.EndsAtUtc:u}; {(proved is { } shown ? $"cycle {shown.Cycle}" : "not taken")}.");
        }

        if (PlacardObservations.From(reading, worldId, Region, proved?.Cycle) is { } observation)
        {
            var rejection = feed.MergeLocal(observation);
            RunLog.Info($"Plot store: placard observation of {observation.Key} {(rejection == MergeRejection.None ? "merged" : $"rejected ({rejection})")}.");
        }

        return proved;
    }

    public void ApplyWardScan(WardScanResult result)
    {
        if (WardObservations.From(result, Region, DateTime.UtcNow) is not { } batch)
        {
            RunLog.Info($"Plot store: the ward scan ({result.Stop}, {result.Wards.Length} ward(s)) held nothing that could be recorded.");
            return;
        }

        var applied = feed.ApplyLocal(batch.Coverage, batch.Observations.AsSpan());
        RunLog.Info($"Plot store: ward scan recorded {applied.Merged} plot(s), {applied.Rejected} rejected, coverage {applied.RecordedOutcome}.");
    }

    public CycleParticipation Participation(ulong contentId, uint homeWorldId, LotterySnapshot? reading, in CyclePosition position)
    {
        var journal = ParticipationRules.FromJournal(JournalSnapshot, contentId, homeWorldId, position.Cycle);
        var read = reading is { } snapshot && snapshot.ContentId == contentId
            ? ParticipationRules.FromReading(snapshot, position, Region, homeWorldId)
            : new SlotEvidence(EntrySlotState.Unknown, null, null, null);
        return ParticipationRules.Resolve(position.Cycle, journal, read);
    }

    public OutstandingEntry Outstanding(ulong contentId, CycleKey cycle) => OutstandingRules.Find(JournalSnapshot, contentId, cycle);

    public CharacterProfile Profile(CharacterContext context, LotterySnapshot? reading, in CyclePosition position)
    {
        var accountGroup = configuration.Characters.TryGetValue(context.ContentId, out var known) ? known.AccountGroup : null;
        var outstanding = OutstandingRules.ToSettlement(Outstanding(context.ContentId, position.Cycle).Kind);
        return CharacterProfileMapping.From(context, Region, accountGroup, outstanding, Participation(context.ContentId, context.HomeWorldId, reading, position));
    }

    // An attempt in question for this cycle holds its entry slot. One from an earlier cycle cannot hold this cycle's
    // slot; it waits for a result check instead (ResultChecks).
    public bool HasPendingAttempt(ulong contentId, CycleKey cycle)
    {
        var attempts = JournalSnapshot.Attempts;
        for (var index = 0; index < attempts.Length; index++)
        {
            var attempt = attempts[index];
            if (attempt.CharacterContentId == contentId
                && attempt.Cycle == cycle
                && attempt.State is AttemptState.Prepared or AttemptState.Dispatched or AttemptState.NeedsReconciliation)
            {
                return true;
            }
        }

        return false;
    }

    // An entry the reading shows that no attempt or receipt of this plugin holds is kept as an imported receipt.
    public async Task ImportFromReading(LotterySnapshot reading, ulong contentId, uint homeWorldId)
    {
        lastReading = reading;
        var position = CurrentPosition(reading.ReadAtUtc);
        if (Journal is not { } journal || ImportedEntries.From(Guid.NewGuid(), contentId, homeWorldId, reading, position) is not { } receipt)
        {
            return;
        }

        if (ParticipationRules.FromJournal(JournalSnapshot, contentId, homeWorldId, position.Cycle).State != EntrySlotState.Unknown)
        {
            return;
        }

        var written = await Task.Run(() => journal.ImportReceipt(receipt, DateTime.UtcNow));
        RunLog.Info($"Journal: the lottery reading shows an entry made outside this plugin for cycle {receipt.Cycle} (number {receipt.EntryNumber?.ToString() ?? "unknown"}, "
                  + $"plot {receipt.Plot?.ToString() ?? "not named"}); import {written}.");
    }
}
