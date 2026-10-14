using System.Threading;
using System.Threading.Tasks;
using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Integration.Model;
using AutoHouseLottery.Core.Persistence;
using AutoHouseLottery.Core.Persistence.Model;
using System.IO;

namespace AutoHouseLottery.Core.Integration;

// The plot store, the lottery calendar and the action journal for the plugin's lifetime, and the one place game
// readings become domain facts. Readings arrive on the framework thread; disk work is sent off it.
internal sealed class HousingContext : IDisposable
{
    public const string JournalFileName = "journal.jsonl";
    public const string ScheduleFileName = "lottery-schedule.json";

    // This plugin runs on the global client; the Chinese and Korean clients are separate Dalamud builds.
    public const GameRegion Region = GameRegion.Global;

    private static readonly JournalSnapshot emptyJournal = new([], [], []);

    private readonly Configuration configuration;
    private readonly string schedulePath;
    private readonly Lock scheduleSaveGate = new();
    private CyclePosition? verified;

    public HousingContext(Configuration configuration, string directory)
    {
        this.configuration = configuration;
        Directory.CreateDirectory(directory);
        schedulePath = Path.Combine(directory, ScheduleFileName);
        if (!LotteryScheduleFile.TryLoad(schedulePath, out var persisted))
        {
            RunLog.Warning($"Lottery calendar: {schedulePath} could not be read; starting from the built-in calendar.");
        }

        Schedules = LotterySchedules.Create(persisted.AsSpan());
        var opened = ActionJournal.Open(Path.Combine(directory, JournalFileName), DateTime.UtcNow);
        Journal = opened.Journal;
        JournalStatus = opened.Status;
        JournalBackupPath = opened.BackupPath;
        RecoveredAttempts = opened.RecoveredAttempts;
        RunLog.Info($"Journal: {opened.Status}{(opened.RecoveredAttempts > 0 ? $", {opened.RecoveredAttempts} uncertain attempt(s) set aside for reconciliation" : string.Empty)}"
                  + $"{(opened.TruncatedTail ? ", a torn last line was dropped" : string.Empty)}{(opened.BackupPath is { } backup ? $", backup at {backup}" : string.Empty)}.");
    }

    public PlotStore Plots { get; } = new();

    public LotterySchedules Schedules { get; }

    public ActionJournal? Journal { get; }

    public JournalOpenStatus JournalStatus { get; }

    public string? JournalBackupPath { get; }

    public int RecoveredAttempts { get; }

    public bool JournalWritable => Journal is { Faulted: false };

    public JournalSnapshot JournalSnapshot => Journal?.Snapshot() ?? emptyJournal;

    public void Dispose() => Journal?.Dispose();

    // A phase the game itself reported wins while it lasts; otherwise the calendar estimate stands.
    public CyclePosition CurrentPosition(DateTime nowUtc)
    {
        if (verified is { } position && position.PhaseStartUtc <= nowUtc && nowUtc < position.PhaseEndUtc)
        {
            return position;
        }

        return Schedules.TryGet(Region, out var schedule) ? LotteryCalendar.Estimate(schedule, nowUtc) : default;
    }

    public LotterySchedule? Schedule => Schedules.TryGet(Region, out var schedule) ? schedule : null;

    // The placard's phase end anchors the calendar, and its agreed fields become a placard observation. Returns the
    // cycle position the placard proved, null when it proved none.
    public CyclePosition? ObservePlacard(PlacardReading reading, uint worldId)
    {
        CyclePosition? proved = null;
        if (PlacardObservations.PhaseOf(reading.Sale) is { } phase)
        {
            var update = Schedules.Observe(Region, phase.Phase, phase.EndsAtUtc, out var position);
            RunLog.Info($"Lottery calendar: placard reports the {phase.Phase} phase ending {phase.EndsAtUtc:u}; {update}{(update == ScheduleUpdate.Rejected ? string.Empty : $", cycle {position.Cycle}")}.");
            if (update != ScheduleUpdate.Rejected)
            {
                proved = position;
                verified = position;
            }

            if (update is ScheduleUpdate.Established or ScheduleUpdate.Reanchored)
            {
                SaveSchedules();
            }
        }

        if (PlacardObservations.From(reading, worldId, Region, proved?.Cycle) is { } observation)
        {
            var rejection = Plots.Merge(observation);
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

        var applied = Plots.Apply(batch.Coverage, batch.Observations.AsSpan());
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

    public bool HasPendingAttempt(ulong contentId)
    {
        var attempts = JournalSnapshot.Attempts;
        for (var index = 0; index < attempts.Length; index++)
        {
            var attempt = attempts[index];
            if (attempt.CharacterContentId == contentId && attempt.State is AttemptState.Prepared or AttemptState.Dispatched or AttemptState.NeedsReconciliation)
            {
                return true;
            }
        }

        return false;
    }

    // An entry the reading shows that no attempt or receipt of this plugin holds is kept as an imported receipt.
    public async Task ImportFromReading(LotterySnapshot reading, ulong contentId, uint homeWorldId)
    {
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

    private void SaveSchedules()
    {
        var schedules = Schedules.All;
        _ = Task.Run(() =>
        {
            lock (scheduleSaveGate)
            {
                if (!LotteryScheduleFile.TrySave(schedulePath, schedules.AsSpan()))
                {
                    RunLog.Warning($"Lottery calendar: {schedulePath} could not be written; the correction holds for this session only.");
                }
            }
        });
    }
}
