using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Game;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Integration.Model;
using AutoHouseLottery.Core.Persistence;
using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Providers;
using AutoHouseLottery.Core.Settlement.Model;

namespace AutoHouseLottery.Core.Integration;

// The action journal for the plugin's lifetime, and the one place game readings become domain facts. The plot store,
// the lottery calendar and the cycle position belong to the housing feed service; readings made in game are routed
// through it, so the listings, the placard and the ward scans share one store and one calendar. Readings arrive on the
// framework thread; merging them into the store and every disk write are sent off it.
internal sealed class HousingContext : IDisposable
{
    public const string JournalFileName = "journal.jsonl";

    // An earlier build kept its own calendar file next to the feed's; the feed's lottery-schedules.json is the only one.
    private const string ObsoleteScheduleFileName = "lottery-schedule.json";

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
        DeleteObsoleteScheduleFile(Path.Combine(directory, ObsoleteScheduleFileName));
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
            _ = Task.Run(() =>
            {
                var rejection = feed.MergeLocal(observation);
                RunLog.Info($"Plot store: placard observation of {observation.Key} {(rejection == MergeRejection.None ? "merged" : $"rejected ({rejection})")}.");
            });
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

        _ = Task.Run(() =>
        {
            var applied = feed.ApplyLocal(batch.Coverage, batch.Observations.AsSpan());
            RunLog.Info($"Plot store: ward scan recorded {applied.Merged} plot(s), {applied.Rejected} rejected, coverage {applied.RecordedOutcome}.");
        });
    }

    public CycleParticipation Participation(ulong contentId, uint homeWorldId, LotterySnapshot? reading, in CyclePosition position)
    {
        var journal = ParticipationRules.FromJournal(JournalSnapshot, contentId, homeWorldId, position.Cycle);
        var read = reading is { } snapshot && snapshot.ContentId == contentId
            ? ParticipationRules.FromReading(snapshot, position, Region, homeWorldId)
            : new SlotEvidence(EntrySlotState.Unknown, null, null, null);
        return ParticipationRules.Resolve(position.Cycle, journal, read);
    }

    public OutstandingEntry Outstanding(ulong contentId, CycleKey cycle) => OutstandingRules.Find(JournalSnapshot, contentId, cycle, LeftToTheGame);

    // An entry nothing here can settle is left to the game: no result was offered at its placard, its outcome could not
    // be read, or its imported address names no single plot.
    private bool LeftToTheGame(EntryReceipt receipt, EntrySettlement settlement)
        => PendingRules.LeftToTheGame(
            settlement.State,
            configuration.AbsenceSighting(receipt.ReceiptId) is not null,
            receipt.Plot is not null || (receipt.AddressText is { } text && ImportedAddress.Resolve(text, receipt.Cycle.Region, receipt.HomeWorldId, DistrictNames.Of) is not null));

    // The player's word that an entry's refund, or its result, was settled in game by hand: recorded as refunded for an
    // amount nobody saw, and reopened like an absence settlement if the game later refuses an entry with LogMessage 6100.
    // Never for an earlier attempt still in question. Off the framework thread.
    public async Task<JournalWrite> MarkSettledInGame(Guid receiptId)
    {
        var snapshot = JournalSnapshot;
        if (Journal is not { } journal
            || SettlementLookups.Receipt(snapshot, receiptId) is not { } receipt
            || SettlementLookups.Settlement(snapshot, receiptId) is not { } settlement
            || !ManualSettlement.CanMarkSettledInGame(settlement.State, SettlementDeadlines.ResultsArrived(receipt.Cycle, CurrentPosition(DateTime.UtcNow)), SettlementLookups.OpenAttempt(snapshot, receiptId) is not null))
        {
            return JournalWrite.IllegalTransition;
        }

        configuration.ForgetAbsenceSighting(receiptId);
        var written = await Task.Run(() =>
        {
            Span<SettlementState> steps = stackalloc SettlementState[SettlementPath.MaximumSteps];
            var count = SettlementPath.Plan(settlement.State, SettlementState.Refunded, steps);
            var result = count < 0 ? JournalWrite.IllegalTransition : JournalWrite.AlreadyRecorded;
            for (var stepIndex = 0; stepIndex < count; stepIndex++)
            {
                result = journal.AdvanceSettlement(receiptId, steps[stepIndex], null, DateTime.UtcNow, byAbsence: stepIndex == count - 1);
            }

            return result;
        });
        RunLog.Info($"Journal: the player marked receipt {receiptId} for {receipt.Plot?.ToString() ?? receipt.AddressText ?? "a plot not named"} as settled in game ({written}); the amount is not known.");
        return written;
    }

    public int? AccountGroup(ulong contentId) => configuration.Characters.TryGetValue(contentId, out var known) ? known.AccountGroup : null;

    public CharacterProfile Profile(CharacterContext context, LotterySnapshot? reading, in CyclePosition position)
    {
        var accountGroup = AccountGroup(context.ContentId);
        var outstanding = OutstandingRules.ToSettlement(Outstanding(context.ContentId, position.Cycle).Kind);
        return CharacterProfileMapping.From(context, Region, accountGroup, outstanding, Participation(context.ContentId, context.HomeWorldId, reading, position));
    }

    // An entry attempt in question for this cycle holds its entry slot. One from an earlier cycle cannot hold this cycle's
    // slot; it waits for a result check instead (ResultChecks). A refund or claim in question is the settlement's to read
    // back, not the entry run's.
    public bool HasPendingAttempt(ulong contentId, CycleKey cycle)
    {
        var attempts = JournalSnapshot.Attempts;
        for (var index = 0; index < attempts.Length; index++)
        {
            var attempt = attempts[index];
            if (attempt.Kind == ActionKind.Entry
                && attempt.CharacterContentId == contentId
                && attempt.Cycle == cycle
                && attempt.State is AttemptState.Prepared or AttemptState.Dispatched or AttemptState.NeedsReconciliation)
            {
                return true;
            }
        }

        return false;
    }

    // An entry the reading shows that no attempt or receipt of this plugin holds is kept as an imported receipt, and a
    // refund the reading proves collected outside the plugin is recorded as settled.
    public async Task ImportFromReading(LotterySnapshot reading, ulong contentId, uint homeWorldId)
    {
        lastReading = reading;
        var position = CurrentPosition(reading.ReadAtUtc);
        var fixes = ManualSettlement.FromReading(JournalSnapshot, reading, position);
        if (!fixes.IsEmpty)
        {
            await ApplySettlementFixes(fixes, "the lottery reading shows no entry");
        }

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

    // Settlements recognised without a visit: the receipt reaches its target through the states the journal requires, and
    // the plugin's own refund or claim still open on it is confirmed with it. Off the framework thread.
    public async Task ApplySettlementFixes(ImmutableArray<SettlementFix> fixes, string evidence)
    {
        if (Journal is not { } journal)
        {
            return;
        }

        await Task.Run(() =>
        {
            for (var index = 0; index < fixes.Length; index++)
            {
                ApplySettlementFix(journal, fixes[index], evidence);
            }
        });
    }

    private static void ApplySettlementFix(ActionJournal journal, SettlementFix fix, string evidence)
    {
        if (fix.ConfirmAttempt is { } attemptId)
        {
            if (SettlementLookups.Attempt(journal.Snapshot(), attemptId) is { } held && SettlementDecisions.NeedsQuestionBeforeConfirm(held.State))
            {
                journal.RequireReconciliation(attemptId, DateTime.UtcNow);
            }

            var confirmed = journal.Confirm(attemptId, null, DateTime.UtcNow);
            RunLog.Info($"Journal: {evidence}, so attempt {attemptId} went through ({confirmed}).");
        }

        if (!journal.TryGetSettlement(fix.ReceiptId, out var current))
        {
            return;
        }

        Span<SettlementState> steps = stackalloc SettlementState[SettlementPath.MaximumSteps];
        var count = SettlementPath.Plan(current.State, fix.Target, steps);
        var written = JournalWrite.IllegalTransition;
        for (var stepIndex = 0; stepIndex < count; stepIndex++)
        {
            written = journal.AdvanceSettlement(fix.ReceiptId, steps[stepIndex], null, DateTime.UtcNow);
        }

        RunLog.Info($"Journal: {evidence}, so receipt {fix.ReceiptId} is settled as {fix.Target} outside a visit ({(count < 0 ? "no legal path from " + current.State : written.ToString())}).");
    }

    // The game refused an entry because a refund or a plot from an earlier entry still waits (LogMessage 6100): every
    // entry of this character that was recorded as settled without its result being read (its placard offered none, or
    // the player said it was settled in game) goes back to an open outcome, so it is checked again and its deposit is not
    // lost from view. The journal writes run off the framework thread; the task is returned so an unload can wait for it.
    // Its sightings are forgotten too, so once the absence rule is validated the same evidence cannot settle it again.
    public Task ReopenAbsenceSettlements(ulong contentId)
    {
        var reopened = ManualSettlement.ReopenedBy6100(JournalSnapshot, contentId);
        if (Journal is not { } journal || reopened.IsEmpty)
        {
            return Task.CompletedTask;
        }

        for (var index = 0; index < reopened.Length; index++)
        {
            configuration.ForgetAbsenceSighting(reopened[index]);
        }

        return Task.Run(() =>
        {
            for (var index = 0; index < reopened.Length; index++)
            {
                var written = journal.ReopenAbsence(reopened[index], DateTime.UtcNow);
                RunLog.Warning($"Journal: the game says an earlier refund or plot still waits, so receipt {reopened[index]}, settled without its result being read, is open again ({written}).");
            }
        });
    }

    private static void DeleteObsoleteScheduleFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
            RunLog.Info($"Removed the obsolete calendar file {path}; the listings' calendar file is the only one.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            RunLog.Warning(exception, $"The obsolete calendar file {path} could not be removed; it is ignored");
        }
    }
}
