using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Shared;

namespace AutoHouseLottery.Core.Persistence;

internal sealed class ActionJournal : IDisposable
{
    private readonly Lock gate = new();
    private readonly string path;
    private readonly int schema;
    private readonly FileStream fileLock;
    private readonly Dictionary<Guid, ActionAttempt> attempts = [];
    private readonly Dictionary<Guid, EntryReceipt> receipts = [];
    private readonly Dictionary<Guid, EntrySettlement> settlements = [];
    private StrongBox<JournalSnapshot> published = new(new JournalSnapshot([], [], []));
    private long committedLength;
    private volatile bool faulted;

    private ActionJournal(string path, int schema, FileStream fileLock)
    {
        this.path = path;
        this.schema = schema;
        this.fileLock = fileLock;
    }

    public bool Faulted => faulted;

    public static JournalOpenResult Open(string path, DateTime nowUtc)
    {
        return Open(path, nowUtc, JournalSchema.Current, JournalMigrations.All);
    }

    internal static JournalOpenResult Open(string path, DateTime nowUtc, int currentSchema, ImmutableArray<JournalMigration> migrations)
    {
        FileStream fileLock;
        try
        {
            fileLock = JournalFile.AcquireLock(path);
        }
        catch (IOException)
        {
            return Failed(JournalOpenStatus.Locked, false);
        }
        catch (UnauthorizedAccessException)
        {
            return Failed(JournalOpenStatus.ReadFailed, false);
        }

        JournalOpenResult result;
        try
        {
            result = Load(path, nowUtc, currentSchema, migrations, fileLock);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            result = Failed(JournalOpenStatus.ReadFailed, false);
        }

        if (result.Journal is null)
        {
            fileLock.Dispose();
        }

        return result;
    }

    public JournalSnapshot Snapshot() => Volatile.Read(ref published).Value;

    public ActionAttempt? FindSlotHolder(OperationKey key)
    {
        var attemptsSnapshot = Snapshot().Attempts;
        for (var index = 0; index < attemptsSnapshot.Length; index++)
        {
            var attempt = attemptsSnapshot[index];
            if (attempt.Key == key && AttemptTransitions.HoldsSlot(attempt.State))
            {
                return attempt;
            }
        }

        return null;
    }

    public bool TryGetSettlement(Guid receiptId, out EntrySettlement settlement)
    {
        var settlementsSnapshot = Snapshot().Settlements;
        for (var index = 0; index < settlementsSnapshot.Length; index++)
        {
            if (settlementsSnapshot[index].ReceiptId == receiptId)
            {
                settlement = settlementsSnapshot[index];
                return true;
            }
        }

        settlement = default;
        return false;
    }

    public JournalWrite Prepare(ActionAttempt attempt)
    {
        lock (gate)
        {
            if (faulted)
            {
                return JournalWrite.Faulted;
            }

            if (!IsWellFormedIntent(attempt) || attempts.ContainsKey(attempt.AttemptId))
            {
                return JournalWrite.Invalid;
            }

            if (attempt.Kind != ActionKind.Entry)
            {
                var outcome = ValidateSettlementIntent(attempt);
                if (outcome != JournalWrite.Written)
                {
                    return outcome;
                }
            }

            if (FindSlotHolderLocked(attempt.Key) is not null || (attempt.Kind == ActionKind.Entry && HasImportedEntryLocked(attempt)))
            {
                return JournalWrite.SlotTaken;
            }

            var prepared = attempt with { UpdatedUtc = attempt.PreparedUtc };
            if (!TryAppend(JournalCodec.Encode(prepared)))
            {
                return JournalWrite.WriteFailed;
            }

            attempts[prepared.AttemptId] = prepared;
            Publish();
            return JournalWrite.Written;
        }
    }

    public JournalWrite RecordDispatch(Guid attemptId, DateTime nowUtc) => Transition(attemptId, AttemptState.Dispatched, nowUtc);

    public JournalWrite Reject(Guid attemptId, DateTime nowUtc) => Transition(attemptId, AttemptState.Rejected, nowUtc);

    public JournalWrite RequireReconciliation(Guid attemptId, DateTime nowUtc) => Transition(attemptId, AttemptState.NeedsReconciliation, nowUtc);

    public JournalWrite Confirm(Guid attemptId, EntryReceipt? receipt, DateTime nowUtc)
    {
        lock (gate)
        {
            if (faulted)
            {
                return JournalWrite.Faulted;
            }

            if (!attempts.TryGetValue(attemptId, out var attempt))
            {
                return JournalWrite.UnknownRecord;
            }

            if (attempt.State == AttemptState.Confirmed)
            {
                return JournalWrite.AlreadyRecorded;
            }

            if (!AttemptTransitions.IsLegal(attempt.State, AttemptState.Confirmed))
            {
                return JournalWrite.IllegalTransition;
            }

            if (attempt.Kind != ActionKind.Entry)
            {
                return CommitTransition(attempt with { State = AttemptState.Confirmed, UpdatedUtc = nowUtc });
            }

            if (receipt is null || !IsReceiptFor(receipt, attempt))
            {
                return JournalWrite.Invalid;
            }

            var linkedReceipt = receipt with { AttemptId = attempt.AttemptId };
            var settlement = settlements.TryGetValue(receipt.ReceiptId, out var existing) ? existing : EntrySettlement.Entered(receipt.ReceiptId, nowUtc);
            var confirmed = attempt with { State = AttemptState.Confirmed, UpdatedUtc = nowUtc, ReceiptId = receipt.ReceiptId };
            if (!TryAppend(JournalCodec.Confirmation(confirmed, linkedReceipt, settlement)))
            {
                return JournalWrite.WriteFailed;
            }

            receipts[linkedReceipt.ReceiptId] = linkedReceipt;
            settlements[settlement.ReceiptId] = settlement;
            attempts[confirmed.AttemptId] = confirmed;
            Publish();
            return JournalWrite.Written;
        }
    }

    // An entry the character's lottery reading shows that no attempt of this plugin made. It takes the cycle's slot
    // like any receipt, so a later run cannot add a second entry for the same character and cycle.
    public JournalWrite ImportReceipt(EntryReceipt receipt, DateTime nowUtc)
    {
        lock (gate)
        {
            if (faulted)
            {
                return JournalWrite.Faulted;
            }

            if (!IsWellFormedImport(receipt) || receipts.ContainsKey(receipt.ReceiptId))
            {
                return JournalWrite.Invalid;
            }

            if (FindSlotHolderLocked(OperationKey.ForEntry(receipt.CharacterContentId, receipt.HomeWorldId, receipt.Cycle)) is not null)
            {
                return JournalWrite.SlotTaken;
            }

            foreach (var existing in receipts.Values)
            {
                if (existing.HoldsSlotOf(receipt.CharacterContentId, receipt.HomeWorldId, receipt.Cycle))
                {
                    return JournalWrite.AlreadyRecorded;
                }
            }

            var settlement = EntrySettlement.Entered(receipt.ReceiptId, nowUtc);
            if (!TryAppend(JournalCodec.Import(receipt, settlement)))
            {
                return JournalWrite.WriteFailed;
            }

            receipts[receipt.ReceiptId] = receipt;
            settlements[settlement.ReceiptId] = settlement;
            Publish();
            return JournalWrite.Written;
        }
    }

    public JournalWrite AdvanceSettlement(Guid receiptId, SettlementState next, long? amount, DateTime nowUtc, bool byAbsence = false)
    {
        lock (gate)
        {
            if (faulted)
            {
                return JournalWrite.Faulted;
            }

            if (!settlements.TryGetValue(receiptId, out var current))
            {
                return JournalWrite.UnknownRecord;
            }

            if (current.State == next)
            {
                return amount is { } given && given != RecordedAmount(current) ? JournalWrite.AmountMismatch : JournalWrite.AlreadyRecorded;
            }

            if (!current.TryAdvance(next, amount, nowUtc, out var advanced) || (byAbsence && next is not (SettlementState.Refunded or SettlementState.Expired)))
            {
                return JournalWrite.IllegalTransition;
            }

            advanced = advanced with { SettledByAbsence = byAbsence };

            if (!TryAppend(JournalCodec.Encode(advanced)))
            {
                return JournalWrite.WriteFailed;
            }

            settlements[receiptId] = advanced;
            Publish();
            var differs = next == SettlementState.Refunded && amount is { } received && received != advanced.ExpectedRefund;
            return differs ? JournalWrite.WrittenAmountDiffers : JournalWrite.Written;
        }
    }

    // The refund deadline a result dialog named, kept with the settlement for its notice and its expiry.
    public JournalWrite RecordRefundDeadline(Guid receiptId, DateTime deadlineUtc, DateTime nowUtc)
    {
        lock (gate)
        {
            if (faulted)
            {
                return JournalWrite.Faulted;
            }

            if (!settlements.TryGetValue(receiptId, out var current))
            {
                return JournalWrite.UnknownRecord;
            }

            if (current.RefundDeadlineUtc == deadlineUtc)
            {
                return JournalWrite.AlreadyRecorded;
            }

            var updated = current with { RefundDeadlineUtc = deadlineUtc, UpdatedUtc = nowUtc };
            if (!TryAppend(JournalCodec.Encode(updated)))
            {
                return JournalWrite.WriteFailed;
            }

            settlements[receiptId] = updated;
            Publish();
            return JournalWrite.Written;
        }
    }

    // A settlement recorded from a placard that no longer offered a result goes back to an open outcome when the game
    // shows the deposit or the plot is still waiting after all. Nothing else that is settled ever reopens.
    public JournalWrite ReopenAbsence(Guid receiptId, DateTime nowUtc)
    {
        lock (gate)
        {
            if (faulted)
            {
                return JournalWrite.Faulted;
            }

            if (!settlements.TryGetValue(receiptId, out var current))
            {
                return JournalWrite.UnknownRecord;
            }

            if (!current.IsSettled || !current.SettledByAbsence)
            {
                return JournalWrite.IllegalTransition;
            }

            var reopened = current with { State = SettlementState.OutcomeUnknown, ReceivedRefund = null, SettledByAbsence = false, UpdatedUtc = nowUtc };
            if (!TryAppend(JournalCodec.Encode(reopened)))
            {
                return JournalWrite.WriteFailed;
            }

            settlements[receiptId] = reopened;
            Publish();
            return JournalWrite.Written;
        }
    }

    // Gil spent travelling for a settlement, kept on the settlement apart from the refund so the net return can be told.
    public JournalWrite AddTravel(Guid receiptId, long spent, DateTime nowUtc)
    {
        lock (gate)
        {
            if (faulted)
            {
                return JournalWrite.Faulted;
            }

            if (spent <= 0)
            {
                return JournalWrite.Invalid;
            }

            if (!settlements.TryGetValue(receiptId, out var current))
            {
                return JournalWrite.UnknownRecord;
            }

            if (!Gil.TryAdd(current.TravelSpent, spent, out var total))
            {
                return JournalWrite.Invalid;
            }

            var updated = current with { TravelSpent = total, UpdatedUtc = nowUtc };
            if (!TryAppend(JournalCodec.Encode(updated)))
            {
                return JournalWrite.WriteFailed;
            }

            settlements[receiptId] = updated;
            Publish();
            return JournalWrite.Written;
        }
    }

    // The plot of an entry imported from the Timers text, once a result read at that plot has proved it. Only an imported
    // receipt that names no plot yet can be given one, and only a plot on its own home world and region.
    public JournalWrite ResolvePlot(Guid receiptId, PlotKey plot)
    {
        lock (gate)
        {
            if (faulted)
            {
                return JournalWrite.Faulted;
            }

            if (!receipts.TryGetValue(receiptId, out var receipt))
            {
                return JournalWrite.UnknownRecord;
            }

            if (receipt.Plot is { } known)
            {
                return known == plot ? JournalWrite.AlreadyRecorded : JournalWrite.IllegalTransition;
            }

            if (!receipt.Imported || !plot.IsValid || plot.WorldId != receipt.HomeWorldId || plot.Region != receipt.Cycle.Region)
            {
                return JournalWrite.Invalid;
            }

            var resolved = receipt with { Plot = plot };
            if (!TryAppend(JournalCodec.Encode(resolved)))
            {
                return JournalWrite.WriteFailed;
            }

            receipts[receiptId] = resolved;
            Publish();
            return JournalWrite.Written;
        }
    }

    public JournalWrite Compact(DateTime pruneSettledBeforeUtc, CycleKey currentCycle, out int prunedReceipts)
    {
        lock (gate)
        {
            prunedReceipts = 0;
            if (faulted)
            {
                return JournalWrite.Faulted;
            }

            var prunable = new HashSet<Guid>();
            foreach (var receipt in receipts.Values)
            {
                if (IsPrunable(receipt, pruneSettledBeforeUtc, currentCycle))
                {
                    prunable.Add(receipt.ReceiptId);
                }
            }

            var keptAttempts = new List<ActionAttempt>(attempts.Count);
            foreach (var attempt in attempts.Values)
            {
                if (!IsPrunable(attempt, prunable, pruneSettledBeforeUtc))
                {
                    keptAttempts.Add(attempt);
                }
            }

            if (!TryRewrite(prunable, keptAttempts))
            {
                return JournalWrite.WriteFailed;
            }

            foreach (var receiptId in prunable)
            {
                receipts.Remove(receiptId);
                settlements.Remove(receiptId);
            }

            attempts.Clear();
            for (var index = 0; index < keptAttempts.Count; index++)
            {
                attempts[keptAttempts[index].AttemptId] = keptAttempts[index];
            }

            prunedReceipts = prunable.Count;
            Publish();
            return JournalWrite.Written;
        }
    }

    // A write still under way when the plugin unloads must not land after the next load has opened the file, so the
    // journal refuses every write from here on; the next open recovers from what is on disk.
    public void Dispose()
    {
        lock (gate)
        {
            faulted = true;
            fileLock.Dispose();
        }
    }

    private static JournalOpenResult Load(string path, DateTime nowUtc, int currentSchema, ImmutableArray<JournalMigration> migrations, FileStream fileLock)
    {
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
        {
            return Create(path, currentSchema, fileLock);
        }

        var file = JournalFile.Read(path);
        if (file.Lines.IsEmpty
            || !TryParse(file.Content.AsSpan(file.Lines[0]), out var header)
            || header.Kind != JournalRecordKind.Header
            || header.Schema is not { } fileSchema)
        {
            return Failed(JournalOpenStatus.Corrupt, file.TruncatedTail);
        }

        if (fileSchema > currentSchema)
        {
            return Failed(JournalOpenStatus.NewerSchema, file.TruncatedTail);
        }

        if (fileSchema < currentSchema)
        {
            return Migrate(path, nowUtc, file, fileSchema, currentSchema, migrations, fileLock);
        }

        var journal = new ActionJournal(path, currentSchema, fileLock);
        var completeLength = file.CompleteLength;
        for (var lineIndex = 1; lineIndex < file.Lines.Length; lineIndex++)
        {
            var line = file.Lines[lineIndex];
            if (!TryParse(file.Content.AsSpan(line), out var record))
            {
                if (lineIndex != file.Lines.Length - 1)
                {
                    return Failed(JournalOpenStatus.Corrupt, file.TruncatedTail);
                }

                completeLength = line.Start.Value;
                break;
            }

            if (!journal.TryApply(record))
            {
                return Failed(JournalOpenStatus.Corrupt, file.TruncatedTail);
            }
        }

        var torn = completeLength != file.Content.Length;
        if (torn)
        {
            JournalFile.TruncateDurably(path, completeLength);
        }

        journal.committedLength = completeLength;
        return journal.Recover(nowUtc, JournalOpenStatus.Opened, torn, null);
    }

    private static JournalOpenResult Migrate(
        string path,
        DateTime nowUtc,
        JournalLines file,
        int fileSchema,
        int currentSchema,
        ImmutableArray<JournalMigration> migrations,
        FileStream fileLock)
    {
        var nodes = new List<JsonObject>(file.Lines.Length - 1);
        for (var lineIndex = 1; lineIndex < file.Lines.Length; lineIndex++)
        {
            if (TryParseNode(file.Content.AsSpan(file.Lines[lineIndex]), out var node))
            {
                nodes.Add(node);
                continue;
            }

            if (lineIndex != file.Lines.Length - 1)
            {
                return Failed(JournalOpenStatus.Corrupt, file.TruncatedTail);
            }
        }

        var backupPath = JournalFile.BackUp(path, fileSchema, nowUtc);
        var journal = new ActionJournal(path, currentSchema, fileLock);
        for (var stepSchema = fileSchema; stepSchema < currentSchema; stepSchema++)
        {
            if (!TryUpgrade(nodes, stepSchema, migrations))
            {
                return new JournalOpenResult(JournalOpenStatus.MigrationFailed, null, 0, file.TruncatedTail, backupPath);
            }
        }

        for (var nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
        {
            if (!TryConvert(nodes[nodeIndex], out var record) || !journal.TryApply(record))
            {
                return new JournalOpenResult(JournalOpenStatus.MigrationFailed, null, 0, file.TruncatedTail, backupPath);
            }
        }

        if (!journal.TryRewrite([], [.. journal.attempts.Values]))
        {
            return new JournalOpenResult(JournalOpenStatus.MigrationFailed, null, 0, file.TruncatedTail, backupPath);
        }

        return journal.Recover(nowUtc, JournalOpenStatus.Migrated, file.TruncatedTail, backupPath);
    }

    private static JournalOpenResult Create(string path, int currentSchema, FileStream fileLock)
    {
        var buffer = new ArrayBufferWriter<byte>();
        WriteLines(buffer, JournalCodec.Header(currentSchema));
        JournalFile.RewriteAtomically(path, buffer.WrittenSpan);
        var journal = new ActionJournal(path, currentSchema, fileLock) { committedLength = buffer.WrittenCount };
        return new JournalOpenResult(JournalOpenStatus.Created, journal, 0, false, null);
    }

    private static JournalOpenResult Failed(JournalOpenStatus status, bool truncatedTail) => new(status, null, 0, truncatedTail, null);

    private static bool TryUpgrade(List<JsonObject> nodes, int fromSchema, ImmutableArray<JournalMigration> migrations)
    {
        for (var migrationIndex = 0; migrationIndex < migrations.Length; migrationIndex++)
        {
            var migration = migrations[migrationIndex];
            if (migration.FromSchema != fromSchema)
            {
                continue;
            }

            try
            {
                for (var nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
                {
                    nodes[nodeIndex] = migration.Upgrade(nodes[nodeIndex]);
                }
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                return false;
            }

            return true;
        }

        return false;
    }

    private static bool TryParse(ReadOnlySpan<byte> line, out JournalRecordJson record)
    {
        try
        {
            record = JsonSerializer.Deserialize(line, JournalJsonContext.Default.JournalRecordJson);
            return true;
        }
        catch (JsonException)
        {
            record = default;
            return false;
        }
    }

    private static bool TryParseNode(ReadOnlySpan<byte> line, [NotNullWhen(true)] out JsonObject? node)
    {
        try
        {
            node = JsonNode.Parse(line) as JsonObject;
            return node is not null;
        }
        catch (JsonException)
        {
            node = null;
            return false;
        }
    }

    private static bool TryConvert(JsonObject node, out JournalRecordJson record)
    {
        try
        {
            record = node.Deserialize(JournalJsonContext.Default.JournalRecordJson);
            return true;
        }
        catch (JsonException)
        {
            record = default;
            return false;
        }
    }

    private static void WriteLines(ArrayBufferWriter<byte> buffer, params ReadOnlySpan<JournalRecordJson> records)
    {
        using var writer = new Utf8JsonWriter(buffer);
        for (var index = 0; index < records.Length; index++)
        {
            JsonSerializer.Serialize(writer, records[index], JournalJsonContext.Default.JournalRecordJson);
            writer.Flush();
            writer.Reset();
            buffer.Write([JournalFile.LineFeed]);
        }
    }

    private static bool IsWellFormedIntent(ActionAttempt attempt)
    {
        if (attempt.State != AttemptState.Prepared || attempt.Amount <= 0 || !attempt.Plot.IsValid || !attempt.Cycle.IsValid)
        {
            return false;
        }

        if (attempt.Plot.WorldId != attempt.HomeWorldId || attempt.Plot.Region != attempt.Cycle.Region)
        {
            return false;
        }

        return attempt.Kind == ActionKind.Entry || attempt.ReceiptId is not null;
    }

    private static bool IsWellFormedImport(EntryReceipt receipt)
    {
        if (!receipt.Imported || receipt.AttemptId is not null || !receipt.Cycle.IsValid || receipt.Deposit is <= 0)
        {
            return false;
        }

        return receipt.Plot is not { } plot || (plot.IsValid && plot.WorldId == receipt.HomeWorldId && plot.Region == receipt.Cycle.Region);
    }

    private static bool IsPrunable(ActionAttempt attempt, HashSet<Guid> prunableReceipts, DateTime pruneSettledBeforeUtc)
    {
        if (attempt.State is not (AttemptState.Confirmed or AttemptState.Rejected))
        {
            return false;
        }

        if (attempt.ReceiptId is { } receiptId)
        {
            return prunableReceipts.Contains(receiptId);
        }

        return attempt.State == AttemptState.Rejected && attempt.UpdatedUtc < pruneSettledBeforeUtc;
    }

    private static long? RecordedAmount(in EntrySettlement settlement) => settlement.State switch
    {
        SettlementState.Refunded => settlement.ReceivedRefund,
        SettlementState.LostRefundDue or SettlementState.ForfeitRefundDue or SettlementState.RefundPending => settlement.ExpectedRefund,
        _ => null,
    };

    private bool IsPrunable(EntryReceipt receipt, DateTime pruneSettledBeforeUtc, CycleKey currentCycle)
    {
        return settlements.TryGetValue(receipt.ReceiptId, out var settlement)
            && settlement.IsSettled
            && !settlement.SettledByAbsence
            && settlement.UpdatedUtc < pruneSettledBeforeUtc
            && receipt.Cycle.Region == currentCycle.Region
            && currentCycle.IsLaterThan(receipt.Cycle)
            && (receipt.Imported || IsConfirmedBy(receipt));
    }

    private bool IsConfirmedBy(EntryReceipt receipt)
    {
        return receipt.AttemptId is { } attemptId
            && attempts.TryGetValue(attemptId, out var attempt)
            && attempt.State == AttemptState.Confirmed
            && attempt.ReceiptId == receipt.ReceiptId;
    }

    private bool IsReceiptFor(EntryReceipt receipt, ActionAttempt attempt)
    {
        if (receipt.Imported || !receipt.Matches(attempt) || receipt.Deposit is not { } deposit || deposit <= 0 || deposit > attempt.Amount)
        {
            return false;
        }

        foreach (var existing in receipts.Values)
        {
            var sameReceipt = existing.ReceiptId == receipt.ReceiptId;
            var sameAttempt = existing.AttemptId == attempt.AttemptId;
            if (sameReceipt != sameAttempt)
            {
                return false;
            }
        }

        return true;
    }

    private JournalWrite ValidateSettlementIntent(ActionAttempt attempt)
    {
        var receiptId = attempt.ReceiptId.GetValueOrDefault();
        if (!receipts.TryGetValue(receiptId, out var receipt) || !settlements.TryGetValue(receiptId, out var settlement))
        {
            return JournalWrite.UnknownRecord;
        }

        if (!receipt.Matches(attempt))
        {
            return JournalWrite.Invalid;
        }

        if (attempt.Kind == ActionKind.Claim)
        {
            return settlement.State == SettlementState.WonUnclaimed ? JournalWrite.Written : JournalWrite.IllegalTransition;
        }

        if (settlement.State is not (SettlementState.LostRefundDue or SettlementState.ForfeitRefundDue or SettlementState.RefundPending))
        {
            return JournalWrite.IllegalTransition;
        }

        return settlement.ExpectedRefund is { } expected && attempt.Amount <= expected ? JournalWrite.Written : JournalWrite.AmountMismatch;
    }

    private bool TryApply(in JournalRecordJson record)
    {
        switch (record.Kind)
        {
            case JournalRecordKind.Attempt when record.Attempt is { } json && JournalCodec.TryDecode(json, out var attempt):
                attempts[attempt.AttemptId] = attempt;
                return true;
            case JournalRecordKind.Receipt when record.Receipt is { } json && JournalCodec.TryDecode(json, out var receipt):
                receipts[receipt.ReceiptId] = receipt;
                return true;
            case JournalRecordKind.Settlement when record.Settlement is { } json && JournalCodec.TryDecode(json, out var settlement):
                settlements[settlement.ReceiptId] = settlement;
                return true;
            case JournalRecordKind.Import
                when record.Receipt is { } receiptJson
                && record.Settlement is { } settlementJson
                && JournalCodec.TryDecode(receiptJson, out var imported)
                && imported.Imported
                && JournalCodec.TryDecode(settlementJson, out var importedSettlement):
                receipts[imported.ReceiptId] = imported;
                settlements[importedSettlement.ReceiptId] = importedSettlement;
                return true;
            case JournalRecordKind.Confirmation
                when record.Attempt is { } attemptJson
                && record.Receipt is { } receiptJson
                && record.Settlement is { } settlementJson
                && JournalCodec.TryDecode(attemptJson, out var confirmed)
                && JournalCodec.TryDecode(receiptJson, out var confirmedReceipt)
                && JournalCodec.TryDecode(settlementJson, out var opened):
                attempts[confirmed.AttemptId] = confirmed;
                receipts[confirmedReceipt.ReceiptId] = confirmedReceipt;
                settlements[opened.ReceiptId] = opened;
                return true;
            default:
                return false;
        }
    }

    private JournalOpenResult Recover(DateTime nowUtc, JournalOpenStatus status, bool truncatedTail, string? backupPath)
    {
        var uncertain = new List<ActionAttempt>();
        foreach (var attempt in attempts.Values)
        {
            if (AttemptTransitions.IsUncertain(attempt.State))
            {
                uncertain.Add(attempt with { State = AttemptState.NeedsReconciliation, UpdatedUtc = nowUtc });
            }
        }

        var persisted = true;
        if (uncertain.Count > 0)
        {
            var buffer = new ArrayBufferWriter<byte>();
            for (var index = 0; index < uncertain.Count; index++)
            {
                WriteLines(buffer, JournalCodec.Encode(uncertain[index]));
            }

            persisted = TryAppendBytes(buffer.WrittenSpan);
            for (var index = 0; index < uncertain.Count; index++)
            {
                attempts[uncertain[index].AttemptId] = uncertain[index];
            }
        }

        Publish();
        var finalStatus = persisted ? status : JournalOpenStatus.RecoveryFailed;
        return new JournalOpenResult(finalStatus, this, uncertain.Count, truncatedTail, backupPath);
    }

    private JournalWrite Transition(Guid attemptId, AttemptState next, DateTime nowUtc)
    {
        lock (gate)
        {
            if (faulted)
            {
                return JournalWrite.Faulted;
            }

            if (!attempts.TryGetValue(attemptId, out var attempt))
            {
                return JournalWrite.UnknownRecord;
            }

            if (attempt.State == next)
            {
                return JournalWrite.AlreadyRecorded;
            }

            if (!AttemptTransitions.IsLegal(attempt.State, next))
            {
                return JournalWrite.IllegalTransition;
            }

            return CommitTransition(attempt with { State = next, UpdatedUtc = nowUtc });
        }
    }

    private JournalWrite CommitTransition(ActionAttempt updated)
    {
        if (!TryAppend(JournalCodec.Encode(updated)))
        {
            return JournalWrite.WriteFailed;
        }

        attempts[updated.AttemptId] = updated;
        Publish();
        return JournalWrite.Written;
    }

    private bool HasImportedEntryLocked(ActionAttempt attempt)
    {
        foreach (var receipt in receipts.Values)
        {
            if (receipt.Imported && receipt.HoldsSlotOf(attempt.CharacterContentId, attempt.HomeWorldId, attempt.Cycle))
            {
                return true;
            }
        }

        return false;
    }

    private ActionAttempt? FindSlotHolderLocked(OperationKey key)
    {
        foreach (var attempt in attempts.Values)
        {
            if (attempt.Key == key && AttemptTransitions.HoldsSlot(attempt.State))
            {
                return attempt;
            }
        }

        return null;
    }

    private bool TryAppend(in JournalRecordJson record)
    {
        var buffer = new ArrayBufferWriter<byte>();
        WriteLines(buffer, record);
        return TryAppendBytes(buffer.WrittenSpan);
    }

    private bool TryAppendBytes(ReadOnlySpan<byte> bytes)
    {
        try
        {
            committedLength = JournalFile.AppendDurably(path, committedLength, bytes);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            faulted = true;
            return false;
        }
    }

    private bool TryRewrite(HashSet<Guid> excludedReceipts, List<ActionAttempt> keptAttempts)
    {
        var buffer = new ArrayBufferWriter<byte>();
        WriteLines(buffer, JournalCodec.Header(schema));
        foreach (var receipt in receipts.Values)
        {
            if (!excludedReceipts.Contains(receipt.ReceiptId))
            {
                WriteLines(buffer, JournalCodec.Encode(receipt));
            }
        }

        foreach (var settlement in settlements.Values)
        {
            if (!excludedReceipts.Contains(settlement.ReceiptId))
            {
                WriteLines(buffer, JournalCodec.Encode(settlement));
            }
        }

        for (var index = 0; index < keptAttempts.Count; index++)
        {
            WriteLines(buffer, JournalCodec.Encode(keptAttempts[index]));
        }

        try
        {
            JournalFile.RewriteAtomically(path, buffer.WrittenSpan);
            committedLength = buffer.WrittenCount;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            faulted = true;
            return false;
        }
    }

    private void Publish()
    {
        var orphans = ImmutableArray.CreateBuilder<Guid>();
        foreach (var receipt in receipts.Values)
        {
            if ((!receipt.Imported && !IsConfirmedBy(receipt)) || !settlements.ContainsKey(receipt.ReceiptId))
            {
                orphans.Add(receipt.ReceiptId);
            }
        }

        var snapshot = new JournalSnapshot([.. attempts.Values], [.. receipts.Values], [.. settlements.Values]) { OrphanedReceipts = orphans.ToImmutable() };
        Volatile.Write(ref published, new StrongBox<JournalSnapshot>(snapshot));
    }
}
