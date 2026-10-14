using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence;
using AutoHouseLottery.Core.Persistence.Model;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests;

public sealed class ActionJournalTests : IDisposable
{
    private const ulong ContentId = 4_000_000_001;
    private static readonly DateTime now = ObservedAt;

    private readonly string directory = Path.Combine(Path.GetTempPath(), "ahl-journal-" + Guid.NewGuid().ToString("N"));
    private readonly string path;
    private readonly List<ActionJournal> opened = [];

    public ActionJournalTests()
    {
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "journal.jsonl");
    }

    public void Dispose()
    {
        for (var index = 0; index < opened.Count; index++)
        {
            opened[index].Dispose();
        }

        if (File.Exists(path))
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }

        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void IntentIsOnDiskBeforePrepareReturns()
    {
        var journal = Create();
        var intent = EntryIntent(Plot(12, 5));

        Assert.Equal(JournalWrite.Written, journal.Prepare(intent));

        Assert.Contains(intent.AttemptId.ToString(), File.ReadAllText(path));
        Assert.Equal(intent.Key, Assert.Single(Reopen(journal).Journal!.Snapshot().Attempts).Key);
    }

    [Fact]
    public void FailedWriteHaltsEveryFurtherWrite()
    {
        var journal = Create();
        var first = EntryIntent(Plot(12, 5));
        journal.Prepare(first);
        File.SetAttributes(path, FileAttributes.ReadOnly);

        Assert.Equal(JournalWrite.WriteFailed, journal.Prepare(EntryIntent(Plot(12, 6)) with { CharacterContentId = ContentId + 1 }));
        File.SetAttributes(path, FileAttributes.Normal);

        Assert.True(journal.Faulted);
        Assert.Single(journal.Snapshot().Attempts);
        Assert.Equal(JournalWrite.Faulted, journal.Prepare(EntryIntent(Plot(12, 7)) with { CharacterContentId = ContentId + 2 }));
        Assert.Equal(JournalWrite.Faulted, journal.RecordDispatch(first.AttemptId, now));
        Assert.Equal(JournalWrite.Faulted, journal.Reject(first.AttemptId, now));
        Assert.Equal(JournalWrite.Faulted, journal.Confirm(first.AttemptId, Receipt(first), now));
        Assert.Equal(JournalWrite.Faulted, journal.AdvanceSettlement(Guid.NewGuid(), SettlementState.ResultsPending, null, now));
        Assert.Equal(JournalWrite.Faulted, journal.Compact(now, SecondCycle, out _));
        Assert.False(Reopen(journal).Journal!.Faulted);
    }

    [Fact]
    public void EntrySlotSurvivesAPlotOrBuyerChange()
    {
        var journal = Create();
        var first = EntryIntent(Plot(12, 5));
        journal.Prepare(first);
        journal.RecordDispatch(first.AttemptId, now);

        Assert.Equal(JournalWrite.SlotTaken, journal.Prepare(EntryIntent(Plot(3, 40))));
        Assert.Equal(JournalWrite.SlotTaken, journal.Prepare(EntryIntent(Plot(12, 5)) with { Buyer = BuyerType.FreeCompany }));

        journal.RequireReconciliation(first.AttemptId, now);
        Assert.Equal(JournalWrite.SlotTaken, journal.Prepare(EntryIntent(Plot(3, 40))));
        Assert.Equal(first.AttemptId, journal.FindSlotHolder(first.Key)!.AttemptId);
    }

    [Fact]
    public void OnlyAConclusiveRejectionReleasesTheSlot()
    {
        var journal = Create();
        var first = EntryIntent(Plot(12, 5));
        journal.Prepare(first);

        Assert.Equal(JournalWrite.Written, journal.Reject(first.AttemptId, now));
        Assert.Equal(JournalWrite.Written, journal.Prepare(EntryIntent(Plot(12, 6))));
    }

    [Fact]
    public void NextCycleHasItsOwnSlot()
    {
        var journal = Create();
        journal.Prepare(EntryIntent(Plot(12, 5)));

        Assert.Equal(JournalWrite.Written, journal.Prepare(EntryIntent(Plot(12, 5)) with { Cycle = SecondCycle }));
    }

    [Fact]
    public void IntentForAPlotOffTheHomeWorldOrRegionIsInvalid()
    {
        var journal = Create();

        Assert.Equal(JournalWrite.Invalid, journal.Prepare(EntryIntent(Plot(12, 5, world: OtherWorld))));
        Assert.Equal(JournalWrite.Invalid, journal.Prepare(EntryIntent(Plot(12, 5)) with { Cycle = new CycleKey(GameRegion.China, 1) }));
    }

    [Fact]
    public void RestartTurnsPreparedAndDispatchedAttemptsIntoReconciliation()
    {
        var journal = Create();
        var prepared = EntryIntent(Plot(12, 5));
        var dispatched = EntryIntent(Plot(12, 6)) with { CharacterContentId = ContentId + 1 };
        journal.Prepare(prepared);
        journal.Prepare(dispatched);
        journal.RecordDispatch(dispatched.AttemptId, now);

        var restarted = Reopen(journal);

        Assert.Equal(JournalOpenStatus.Opened, restarted.Status);
        Assert.Equal(2, restarted.RecoveredAttempts);
        var attempts = restarted.Journal!.Snapshot().Attempts;
        for (var index = 0; index < attempts.Length; index++)
        {
            Assert.Equal(AttemptState.NeedsReconciliation, attempts[index].State);
        }

        Assert.Equal(JournalWrite.SlotTaken, restarted.Journal.Prepare(EntryIntent(Plot(1, 1))));
        Assert.Equal(0, Reopen(restarted.Journal).RecoveredAttempts);
    }

    [Fact]
    public void FailedRecoveryWriteIsReportedAndStillTreatsAttemptsAsUncertain()
    {
        var journal = Create();
        journal.Prepare(EntryIntent(Plot(12, 5)));
        journal.Dispose();
        File.SetAttributes(path, FileAttributes.ReadOnly);

        var reopened = Open();

        Assert.Equal(JournalOpenStatus.RecoveryFailed, reopened.Status);
        Assert.True(reopened.Journal!.Faulted);
        Assert.Equal(AttemptState.NeedsReconciliation, Assert.Single(reopened.Journal.Snapshot().Attempts).State);
        Assert.Equal(JournalWrite.Faulted, reopened.Journal.Prepare(EntryIntent(Plot(1, 1)) with { CharacterContentId = ContentId + 1 }));
    }

    [Fact]
    public void TruncatedLastLineIsDroppedAndTheRestRecovers()
    {
        var journal = Create();
        var intent = EntryIntent(Plot(12, 5));
        journal.Prepare(intent);
        journal.Dispose();
        File.AppendAllText(path, "{\"kind\":\"Attempt\",\"attempt\":{\"id\":\"");

        var reopened = Open();

        Assert.Equal(JournalOpenStatus.Opened, reopened.Status);
        Assert.True(reopened.TruncatedTail);
        Assert.Equal(intent.AttemptId, Assert.Single(reopened.Journal!.Snapshot().Attempts).AttemptId);
        Assert.EndsWith("\n", File.ReadAllText(path));
        Assert.False(Reopen(reopened.Journal).TruncatedTail);
    }

    [Fact]
    public void UnreadableLastCompleteLineIsATornTailButEarlierDamageIsCorrupt()
    {
        var journal = Create();
        journal.Prepare(EntryIntent(Plot(12, 5)));
        journal.Dispose();
        File.AppendAllText(path, "\0\0\0\0\n");

        var torn = Open();
        Assert.Equal(JournalOpenStatus.Opened, torn.Status);
        Assert.True(torn.TruncatedTail);
        torn.Journal!.Dispose();

        var intact = File.ReadAllText(path);
        File.WriteAllText(path, intact + "not json\n" + intact.Split('\n')[1] + "\n");

        var corrupt = Open();
        Assert.Equal(JournalOpenStatus.Corrupt, corrupt.Status);
        Assert.Null(corrupt.Journal);
    }

    [Fact]
    public void TornConfirmationLineLeavesTheEntryForReconciliationWithoutAnOrphan()
    {
        var journal = Create();
        var intent = EntryIntent(Plot(12, 5));
        journal.Prepare(intent);
        journal.RecordDispatch(intent.AttemptId, now);
        var receipt = Receipt(intent);
        journal.Confirm(intent.AttemptId, receipt, now);
        journal.Dispose();
        var bytes = File.ReadAllBytes(path);
        var lastLineStart = Array.LastIndexOf(bytes, JournalFile.LineFeed, bytes.Length - 2) + 1;
        File.WriteAllBytes(path, bytes.AsSpan(0, lastLineStart + (bytes.Length - lastLineStart) / 2).ToArray());

        var reopened = Open();

        var snapshot = reopened.Journal!.Snapshot();
        Assert.Equal(AttemptState.NeedsReconciliation, Assert.Single(snapshot.Attempts).State);
        Assert.Empty(snapshot.Receipts);
        Assert.Empty(snapshot.OrphanedReceipts);
        Assert.Equal(JournalWrite.Written, reopened.Journal.Confirm(intent.AttemptId, receipt, now));
        Assert.Single(reopened.Journal.Snapshot().Receipts);
    }

    [Fact]
    public void LegacyReceiptWithoutItsConfirmationIsFlaggedAndOnlyAdopted()
    {
        var journal = Create();
        var intent = EntryIntent(Plot(12, 5));
        journal.Prepare(intent);
        journal.RecordDispatch(intent.AttemptId, now);
        journal.Dispose();
        var receipt = Receipt(intent) with { AttemptId = intent.AttemptId };
        var line = JsonSerializer.Serialize(JournalCodec.Encode(receipt), JournalJsonContext.Default.JournalRecordJson);
        File.AppendAllText(path, line + "\n");

        var reopened = Open();
        var recovered = reopened.Journal!;

        Assert.Equal(receipt.ReceiptId, Assert.Single(recovered.Snapshot().OrphanedReceipts));
        Assert.Equal(JournalWrite.Invalid, recovered.Confirm(intent.AttemptId, Receipt(intent), now));
        Assert.Equal(JournalWrite.Written, recovered.Confirm(intent.AttemptId, receipt, now));
        Assert.Empty(recovered.Snapshot().OrphanedReceipts);
        Assert.Single(recovered.Snapshot().Receipts);
    }

    [Fact]
    public void PartialWriteFollowedByAnAppendKeepsTheJournalReadable()
    {
        var journal = Create();
        var first = EntryIntent(Plot(12, 5));
        journal.Prepare(first);
        File.AppendAllText(path, "{\"kind\":\"Attempt\",\"atte");

        Assert.Equal(JournalWrite.Written, journal.Prepare(EntryIntent(Plot(12, 6)) with { CharacterContentId = ContentId + 1 }));

        var reopened = Reopen(journal);
        Assert.Equal(JournalOpenStatus.Opened, reopened.Status);
        Assert.False(reopened.TruncatedTail);
        Assert.Equal(2, reopened.Journal!.Snapshot().Attempts.Length);
    }

    [Fact]
    public void ReceiptObservedButNotPersistedIsRecoveredFromThePendingIntent()
    {
        var journal = Create();
        var intent = EntryIntent(Plot(12, 5));
        journal.Prepare(intent);
        journal.RecordDispatch(intent.AttemptId, now);
        var receipt = Receipt(intent);
        File.SetAttributes(path, FileAttributes.ReadOnly);

        Assert.Equal(JournalWrite.WriteFailed, journal.Confirm(intent.AttemptId, receipt, now));
        Assert.Equal(AttemptState.Dispatched, Assert.Single(journal.Snapshot().Attempts).State);
        Assert.Equal(JournalWrite.Faulted, journal.Prepare(EntryIntent(Plot(12, 6)) with { CharacterContentId = ContentId + 1 }));
        File.SetAttributes(path, FileAttributes.Normal);

        var reopened = Reopen(journal);

        Assert.Equal(1, reopened.RecoveredAttempts);
        Assert.Equal(JournalWrite.SlotTaken, reopened.Journal!.Prepare(EntryIntent(Plot(3, 3))));
        Assert.Equal(JournalWrite.Written, reopened.Journal.Confirm(intent.AttemptId, receipt, now));
    }

    [Fact]
    public void SecondInstanceOnTheSameFileIsRefused()
    {
        var journal = Create();

        Assert.Equal(JournalOpenStatus.Locked, ActionJournal.Open(path, now).Status);

        journal.Dispose();
        Assert.Equal(JournalOpenStatus.Opened, Open().Status);
    }

    [Fact]
    public void ConcurrentIntentsForOneSlotAreWrittenOnce()
    {
        var journal = Create();
        var results = new JournalWrite[16];

        Parallel.For(0, results.Length, index =>
        {
            results[index] = journal.Prepare(EntryIntent(Plot(1, index + 1)));
            Assert.True(journal.Snapshot().Attempts.Length <= 1);
        });

        var written = 0;
        for (var index = 0; index < results.Length; index++)
        {
            written += results[index] == JournalWrite.Written ? 1 : 0;
            Assert.True(results[index] is JournalWrite.Written or JournalWrite.SlotTaken);
        }

        Assert.Equal(1, written);
        Assert.Single(Reopen(journal).Journal!.Snapshot().Attempts);
    }

    [Fact]
    public void NonEmptyFileWithoutACompleteLineIsCorruptNotRecreated()
    {
        File.WriteAllBytes(path, new byte[4096]);

        var reopened = Open();

        Assert.Equal(JournalOpenStatus.Corrupt, reopened.Status);
        Assert.Equal(4096, new FileInfo(path).Length);
    }

    [Fact]
    public void JournalFromANewerVersionIsNotTouched()
    {
        File.WriteAllText(path, "{\"kind\":\"Header\",\"schema\":99}\n");

        Assert.Equal(JournalOpenStatus.NewerSchema, Open().Status);
        Assert.Equal("{\"kind\":\"Header\",\"schema\":99}\n", File.ReadAllText(path));
    }

    [Fact]
    public void MigrationBacksUpTheOriginalBeforeUpgrading()
    {
        var journal = Create();
        var intent = EntryIntent(Plot(12, 5));
        journal.Prepare(intent);
        journal.Reject(intent.AttemptId, now);
        journal.Dispose();
        var original = File.ReadAllBytes(path);

        var migrated = Track(ActionJournal.Open(path, now, 2, [new JournalMigration(1, AddGilBefore)]));

        Assert.Equal(JournalOpenStatus.Migrated, migrated.Status);
        Assert.Equal(original, File.ReadAllBytes(migrated.BackupPath!));
        Assert.Equal(42, Assert.Single(migrated.Journal!.Snapshot().Attempts).GilBefore);
        Assert.StartsWith("{\"kind\":\"Header\",\"schema\":2}", File.ReadAllText(path));
        migrated.Journal.Dispose();
        Assert.Equal(JournalOpenStatus.Opened, Track(ActionJournal.Open(path, now, 2, [])).Status);
    }

    [Fact]
    public void MissingMigrationStepLeavesTheOriginalAndItsBackup()
    {
        var journal = Create();
        journal.Prepare(EntryIntent(Plot(12, 5)));
        journal.Dispose();
        var original = File.ReadAllBytes(path);

        var migrated = Track(ActionJournal.Open(path, now, 3, [new JournalMigration(1, AddGilBefore)]));

        Assert.Equal(JournalOpenStatus.MigrationFailed, migrated.Status);
        Assert.Null(migrated.Journal);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.True(File.Exists(migrated.BackupPath));
    }

    [Fact]
    public void ConfirmationRecordsTheMatchingReceiptOnceInOneLine()
    {
        var journal = Create();
        var intent = EntryIntent(Plot(12, 5));
        journal.Prepare(intent);
        journal.RecordDispatch(intent.AttemptId, now);
        var receipt = Receipt(intent);
        var linesBefore = File.ReadAllLines(path).Length;

        Assert.Equal(JournalWrite.Invalid, journal.Confirm(intent.AttemptId, receipt with { Plot = Plot(12, 6) }, now));
        Assert.Equal(JournalWrite.Invalid, journal.Confirm(intent.AttemptId, receipt with { Deposit = Deposit + 1 }, now));
        Assert.Equal(JournalWrite.Written, journal.Confirm(intent.AttemptId, receipt, now));
        Assert.Equal(JournalWrite.AlreadyRecorded, journal.Confirm(intent.AttemptId, receipt, now));
        Assert.Equal(JournalWrite.IllegalTransition, journal.RecordDispatch(intent.AttemptId, now));

        Assert.Equal(linesBefore + 1, File.ReadAllLines(path).Length);
        Assert.True(journal.TryGetSettlement(receipt.ReceiptId, out var settlement));
        Assert.Equal(SettlementState.Entered, settlement.State);
        var reopened = Reopen(journal).Journal!.Snapshot();
        Assert.Equal(receipt.ReceiptId, Assert.Single(reopened.Receipts).ReceiptId);
        Assert.Equal(AttemptState.Confirmed, Assert.Single(reopened.Attempts).State);
    }

    [Fact]
    public void RefundAndClaimIntentsFollowTheSettledOutcome()
    {
        var journal = Create();
        var lost = ConfirmedEntry(journal, Plot(12, 5));
        var won = ConfirmedEntry(journal, Plot(12, 6), ContentId + 1);

        Assert.Equal(JournalWrite.IllegalTransition, journal.Prepare(SettlementIntent(ActionKind.Refund, lost)));
        Assert.Equal(JournalWrite.UnknownRecord, journal.Prepare(SettlementIntent(ActionKind.Refund, lost) with { ReceiptId = Guid.NewGuid() }));

        Walk(journal, won.ReceiptId, SettlementState.ResultsPending, SettlementState.InspectingResult, SettlementState.WonUnclaimed);
        Assert.Equal(JournalWrite.IllegalTransition, journal.Prepare(SettlementIntent(ActionKind.Refund, won)));
        Assert.Equal(JournalWrite.Written, journal.Prepare(SettlementIntent(ActionKind.Claim, won)));

        Walk(journal, lost.ReceiptId, SettlementState.ResultsPending, SettlementState.InspectingResult, SettlementState.LostRefundDue);
        Assert.Equal(JournalWrite.IllegalTransition, journal.Prepare(SettlementIntent(ActionKind.Claim, lost)));
        Assert.Equal(JournalWrite.AmountMismatch, journal.Prepare(SettlementIntent(ActionKind.Refund, lost) with { Amount = Deposit + 1 }));
        Assert.Equal(JournalWrite.Written, journal.Prepare(SettlementIntent(ActionKind.Refund, lost)));
        Assert.Equal(JournalWrite.SlotTaken, journal.Prepare(SettlementIntent(ActionKind.Refund, lost)));
    }

    [Fact]
    public void RefundAmountDifferentFromTheExpectedOneIsReported()
    {
        var journal = Create();
        var receipt = ConfirmedEntry(journal, Plot(12, 5));
        Walk(journal, receipt.ReceiptId, SettlementState.ResultsPending, SettlementState.InspectingResult, SettlementState.LostRefundDue, SettlementState.RefundPending);

        Assert.Equal(JournalWrite.WrittenAmountDiffers, journal.AdvanceSettlement(receipt.ReceiptId, SettlementState.Refunded, Deposit / 2, now));
        Assert.Equal(JournalWrite.AmountMismatch, journal.AdvanceSettlement(receipt.ReceiptId, SettlementState.Refunded, Deposit, now));
        Assert.Equal(JournalWrite.AlreadyRecorded, journal.AdvanceSettlement(receipt.ReceiptId, SettlementState.Refunded, Deposit / 2, now));
        Assert.True(journal.TryGetSettlement(receipt.ReceiptId, out var settlement));
        Assert.Equal(Deposit / 2, settlement.ReceivedRefund);
        Assert.Equal(Deposit, settlement.ExpectedRefund);
    }

    [Fact]
    public void IllegalSettlementTransitionsAreRefused()
    {
        var journal = Create();
        var receipt = ConfirmedEntry(journal, Plot(12, 5));

        Assert.Equal(JournalWrite.IllegalTransition, journal.AdvanceSettlement(receipt.ReceiptId, SettlementState.Refunded, Deposit, now));
        Assert.Equal(JournalWrite.Written, journal.AdvanceSettlement(receipt.ReceiptId, SettlementState.ResultsPending, null, now));
        Assert.Equal(JournalWrite.UnknownRecord, journal.AdvanceSettlement(Guid.NewGuid(), SettlementState.ResultsPending, null, now));
    }

    [Fact]
    public void CompactionKeepsUnsettledReceiptsAndEveryCurrentCycleSlot()
    {
        var journal = Create();
        var refunded = ConfirmedEntry(journal, Plot(12, 5));
        var unsettled = ConfirmedEntry(journal, Plot(12, 6), ContentId + 1);
        Walk(journal, refunded.ReceiptId, SettlementState.ResultsPending, SettlementState.InspectingResult, SettlementState.LostRefundDue, SettlementState.RefundPending, SettlementState.Refunded);

        Assert.Equal(JournalWrite.Written, journal.Compact(now.AddDays(200), FirstCycle, out var keptCurrentCycle));
        Assert.Equal(0, keptCurrentCycle);
        Assert.Equal(JournalWrite.SlotTaken, journal.Prepare(EntryIntent(Plot(1, 1))));

        Assert.Equal(JournalWrite.Written, journal.Compact(now.AddDays(200), SecondCycle, out var pruned));
        Assert.Equal(1, pruned);
        var reopened = Reopen(journal).Journal!.Snapshot();
        Assert.Equal(unsettled.ReceiptId, Assert.Single(reopened.Receipts).ReceiptId);
        Assert.Equal(unsettled.ReceiptId, Assert.Single(reopened.Attempts).ReceiptId);
        Assert.False(File.Exists(path + ".tmp"));
    }

    private ActionJournal Create()
    {
        var created = Open();
        Assert.Equal(JournalOpenStatus.Created, created.Status);
        return created.Journal!;
    }

    private JournalOpenResult Open() => Track(ActionJournal.Open(path, now));

    private JournalOpenResult Reopen(ActionJournal journal)
    {
        journal.Dispose();
        return Open();
    }

    private JournalOpenResult Track(JournalOpenResult result)
    {
        if (result.Journal is { } journal)
        {
            opened.Add(journal);
        }

        return result;
    }

    private static ActionAttempt EntryIntent(PlotKey plot) => new()
    {
        AttemptId = Guid.NewGuid(),
        Kind = ActionKind.Entry,
        RunId = Guid.NewGuid(),
        CharacterContentId = ContentId,
        HomeWorldId = HomeWorld,
        Cycle = FirstCycle,
        Plot = plot,
        Buyer = BuyerType.Private,
        Amount = Deposit,
        PreparedUtc = now,
    };

    private static ActionAttempt SettlementIntent(ActionKind kind, EntryReceipt receipt) => new()
    {
        AttemptId = Guid.NewGuid(),
        Kind = kind,
        RunId = Guid.NewGuid(),
        CharacterContentId = receipt.CharacterContentId,
        HomeWorldId = receipt.HomeWorldId,
        Cycle = receipt.Cycle,
        Plot = receipt.Plot!.Value,
        Buyer = receipt.Buyer,
        Amount = Deposit,
        PreparedUtc = now,
        ReceiptId = receipt.ReceiptId,
    };

    private static EntryReceipt Receipt(ActionAttempt attempt) => new()
    {
        ReceiptId = Guid.NewGuid(),
        CharacterContentId = attempt.CharacterContentId,
        HomeWorldId = attempt.HomeWorldId,
        Cycle = attempt.Cycle,
        Plot = attempt.Plot,
        Buyer = attempt.Buyer,
        Evidence = ReceiptEvidence.NativeConfirmation,
        EntryNumber = 17,
        Deposit = attempt.Amount,
        EnteredUtc = now,
    };

    private static EntryReceipt ConfirmedEntry(ActionJournal journal, PlotKey plot, ulong contentId = ContentId)
    {
        var intent = EntryIntent(plot) with { CharacterContentId = contentId };
        Assert.Equal(JournalWrite.Written, journal.Prepare(intent));
        Assert.Equal(JournalWrite.Written, journal.RecordDispatch(intent.AttemptId, now));
        var receipt = Receipt(intent);
        Assert.Equal(JournalWrite.Written, journal.Confirm(intent.AttemptId, receipt, now));
        return receipt;
    }

    private static void Walk(ActionJournal journal, Guid receiptId, params SettlementState[] steps)
    {
        for (var index = 0; index < steps.Length; index++)
        {
            Assert.Equal(JournalWrite.Written, journal.AdvanceSettlement(receiptId, steps[index], Deposit, now));
        }
    }

    private static JsonObject AddGilBefore(JsonObject record)
    {
        if (record["attempt"] is JsonObject attempt)
        {
            attempt["gilBefore"] = 42;
        }

        return record;
    }
}
