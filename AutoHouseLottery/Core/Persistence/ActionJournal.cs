using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using AutoHouseLottery.Core.Persistence.Model;

namespace AutoHouseLottery.Core.Persistence;

internal sealed class ActionJournal
{
    private readonly Lock gate = new();
    private readonly string path;
    private readonly int schema;
    private readonly Dictionary<Guid, ActionAttempt> attempts = [];
    private readonly Dictionary<Guid, EntryReceipt> receipts = [];
    private readonly Dictionary<Guid, EntrySettlement> settlements = [];
    private bool faulted;

    private ActionJournal(string path, int schema)
    {
        this.path = path;
        this.schema = schema;
    }

    public bool Faulted
    {
        get
        {
            lock (gate)
            {
                return faulted;
            }
        }
    }

    public static JournalOpenResult Open(string path, DateTime nowUtc)
    {
        return Open(path, nowUtc, JournalSchema.Current, JournalMigrations.All);
    }

    internal static JournalOpenResult Open(string path, DateTime nowUtc, int currentSchema, ImmutableArray<JournalMigration> migrations)
    {
        try
        {
            return Load(path, nowUtc, currentSchema, migrations);
        }
        catch (IOException)
        {
            return new JournalOpenResult(JournalOpenStatus.ReadFailed, null, 0, false, null);
        }
        catch (UnauthorizedAccessException)
        {
            return new JournalOpenResult(JournalOpenStatus.ReadFailed, null, 0, false, null);
        }
    }

    public JournalSnapshot Snapshot()
    {
        lock (gate)
        {
            return new JournalSnapshot([.. attempts.Values], [.. receipts.Values], [.. settlements.Values]);
        }
    }

    public ActionAttempt? FindSlotHolder(OperationKey key)
    {
        lock (gate)
        {
            return FindSlotHolderLocked(key);
        }
    }

    public bool TryGetSettlement(Guid receiptId, out EntrySettlement settlement)
    {
        lock (gate)
        {
            return settlements.TryGetValue(receiptId, out settlement);
        }
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

            if (FindSlotHolderLocked(attempt.Key) is not null)
            {
                return JournalWrite.SlotTaken;
            }

            var prepared = attempt with { UpdatedUtc = attempt.PreparedUtc };
            if (!TryAppend(JournalCodec.Encode(prepared)))
            {
                return JournalWrite.WriteFailed;
            }

            attempts[prepared.AttemptId] = prepared;
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

            if (receipt is null || !receipt.Matches(attempt) || receipts.ContainsKey(receipt.ReceiptId))
            {
                return JournalWrite.Invalid;
            }

            var linkedReceipt = receipt with { AttemptId = attempt.AttemptId };
            var settlement = EntrySettlement.Entered(receipt.ReceiptId, nowUtc);
            var confirmed = attempt with { State = AttemptState.Confirmed, UpdatedUtc = nowUtc, ReceiptId = receipt.ReceiptId };
            if (!TryAppend(JournalCodec.Encode(linkedReceipt), JournalCodec.Encode(settlement), JournalCodec.Encode(confirmed)))
            {
                return JournalWrite.WriteFailed;
            }

            receipts[linkedReceipt.ReceiptId] = linkedReceipt;
            settlements[settlement.ReceiptId] = settlement;
            attempts[confirmed.AttemptId] = confirmed;
            return JournalWrite.Written;
        }
    }

    public JournalWrite AdvanceSettlement(Guid receiptId, SettlementState next, long? amount, DateTime nowUtc)
    {
        lock (gate)
        {
            if (!settlements.TryGetValue(receiptId, out var current))
            {
                return JournalWrite.UnknownRecord;
            }

            if (current.State == next)
            {
                return JournalWrite.AlreadyRecorded;
            }

            if (!current.TryAdvance(next, amount, nowUtc, out var advanced))
            {
                return JournalWrite.IllegalTransition;
            }

            if (!TryAppend(JournalCodec.Encode(advanced)))
            {
                return JournalWrite.WriteFailed;
            }

            settlements[receiptId] = advanced;
            return JournalWrite.Written;
        }
    }

    public JournalWrite Compact(DateTime pruneSettledBeforeUtc, out int prunedReceipts)
    {
        lock (gate)
        {
            prunedReceipts = 0;
            var prunable = new HashSet<Guid>();
            foreach (var settlement in settlements.Values)
            {
                if (settlement.IsSettled && settlement.UpdatedUtc < pruneSettledBeforeUtc)
                {
                    prunable.Add(settlement.ReceiptId);
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
            return JournalWrite.Written;
        }
    }

    private static JournalOpenResult Load(string path, DateTime nowUtc, int currentSchema, ImmutableArray<JournalMigration> migrations)
    {
        if (!File.Exists(path))
        {
            return Create(path, currentSchema, false);
        }

        var file = JournalFile.Read(path);
        if (file.Lines.IsEmpty)
        {
            return Create(path, currentSchema, file.TruncatedTail);
        }

        if (!TryParse(file.Content.AsSpan(file.Lines[0]), out var header) || header.Kind != JournalRecordKind.Header || header.Schema is not { } fileSchema)
        {
            return Failed(JournalOpenStatus.Corrupt, file.TruncatedTail);
        }

        if (fileSchema > currentSchema)
        {
            return Failed(JournalOpenStatus.NewerSchema, file.TruncatedTail);
        }

        if (fileSchema < currentSchema)
        {
            return Migrate(path, nowUtc, file, fileSchema, currentSchema, migrations);
        }

        var journal = new ActionJournal(path, currentSchema);
        for (var lineIndex = 1; lineIndex < file.Lines.Length; lineIndex++)
        {
            if (!TryParse(file.Content.AsSpan(file.Lines[lineIndex]), out var record) || !journal.TryApply(record))
            {
                return Failed(JournalOpenStatus.Corrupt, file.TruncatedTail);
            }
        }

        if (file.TruncatedTail)
        {
            JournalFile.TruncateDurably(path, file.CompleteLength);
        }

        var recovered = journal.RecoverUncertainAttempts(nowUtc);
        return new JournalOpenResult(JournalOpenStatus.Opened, journal, recovered, file.TruncatedTail, null);
    }

    private static JournalOpenResult Migrate(
        string path,
        DateTime nowUtc,
        JournalLines file,
        int fileSchema,
        int currentSchema,
        ImmutableArray<JournalMigration> migrations)
    {
        var nodes = new JsonObject[file.Lines.Length - 1];
        for (var lineIndex = 1; lineIndex < file.Lines.Length; lineIndex++)
        {
            if (!TryParseNode(file.Content.AsSpan(file.Lines[lineIndex]), out var node))
            {
                return Failed(JournalOpenStatus.Corrupt, file.TruncatedTail);
            }

            nodes[lineIndex - 1] = node;
        }

        var backupPath = JournalFile.BackUp(path, fileSchema, nowUtc);
        var journal = new ActionJournal(path, currentSchema);
        for (var stepSchema = fileSchema; stepSchema < currentSchema; stepSchema++)
        {
            if (!TryUpgrade(nodes, stepSchema, migrations))
            {
                return new JournalOpenResult(JournalOpenStatus.MigrationFailed, null, 0, file.TruncatedTail, backupPath);
            }
        }

        for (var nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
        {
            if (!TryConvert(nodes[nodeIndex], out var record) || !journal.TryApply(record))
            {
                return new JournalOpenResult(JournalOpenStatus.MigrationFailed, null, 0, file.TruncatedTail, backupPath);
            }
        }

        if (!journal.TryRewriteAll())
        {
            return new JournalOpenResult(JournalOpenStatus.MigrationFailed, null, 0, file.TruncatedTail, backupPath);
        }

        var recovered = journal.RecoverUncertainAttempts(nowUtc);
        return new JournalOpenResult(JournalOpenStatus.Migrated, journal, recovered, file.TruncatedTail, backupPath);
    }

    private static JournalOpenResult Create(string path, int currentSchema, bool truncatedTail)
    {
        var buffer = new ArrayBufferWriter<byte>();
        WriteLines(buffer, JournalCodec.Header(currentSchema));
        JournalFile.RewriteAtomically(path, buffer.WrittenSpan);
        return new JournalOpenResult(JournalOpenStatus.Created, new ActionJournal(path, currentSchema), 0, truncatedTail, null);
    }

    private static JournalOpenResult Failed(JournalOpenStatus status, bool truncatedTail) => new(status, null, 0, truncatedTail, null);

    private static bool TryUpgrade(JsonObject[] nodes, int fromSchema, ImmutableArray<JournalMigration> migrations)
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
                for (var nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
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

        return attempt.Kind == ActionKind.Entry || attempt.ReceiptId is not null;
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
            default:
                return false;
        }
    }

    private int RecoverUncertainAttempts(DateTime nowUtc)
    {
        var uncertain = new List<ActionAttempt>();
        foreach (var attempt in attempts.Values)
        {
            if (AttemptTransitions.IsUncertain(attempt.State))
            {
                uncertain.Add(attempt with { State = AttemptState.NeedsReconciliation, UpdatedUtc = nowUtc });
            }
        }

        if (uncertain.Count == 0)
        {
            return 0;
        }

        var buffer = new ArrayBufferWriter<byte>();
        for (var index = 0; index < uncertain.Count; index++)
        {
            WriteLines(buffer, JournalCodec.Encode(uncertain[index]));
        }

        if (TryAppendBytes(buffer.WrittenSpan))
        {
            for (var index = 0; index < uncertain.Count; index++)
            {
                attempts[uncertain[index].AttemptId] = uncertain[index];
            }
        }

        return uncertain.Count;
    }

    private JournalWrite Transition(Guid attemptId, AttemptState next, DateTime nowUtc)
    {
        lock (gate)
        {
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
        return JournalWrite.Written;
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

    private bool TryAppend(params ReadOnlySpan<JournalRecordJson> records)
    {
        var buffer = new ArrayBufferWriter<byte>();
        WriteLines(buffer, records);
        return TryAppendBytes(buffer.WrittenSpan);
    }

    private bool TryAppendBytes(ReadOnlySpan<byte> bytes)
    {
        try
        {
            JournalFile.AppendDurably(path, bytes);
            return true;
        }
        catch (IOException)
        {
            faulted = true;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            faulted = true;
            return false;
        }
    }

    private bool TryRewriteAll() => TryRewrite([], [.. attempts.Values]);

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
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
