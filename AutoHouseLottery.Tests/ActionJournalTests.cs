using System.IO;
using System.Text;
using System.Text.Json.Nodes;
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

    public ActionJournalTests()
    {
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "journal.jsonl");
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void IntentIsOnDiskBeforePrepareReturns()
    {
        var journal = OpenJournal();
        var intent = EntryIntent(Plot(12, 5));

        Assert.Equal(JournalWrite.Written, journal.Prepare(intent));

        Assert.Contains(intent.AttemptId.ToString(), File.ReadAllText(path));
        var reopened = ActionJournal.Open(path, now.AddMinutes(1));
        Assert.Equal(intent.Key, Assert.Single(reopened.Journal!.Snapshot().Attempts).Key);
    }

    [Fact]
    public void FailedIntentWriteRefusesDispatchAndHaltsFurtherIntents()
    {
        var journal = OpenJournal();
        Directory.Delete(directory, recursive: true);

        Assert.Equal(JournalWrite.WriteFailed, journal.Prepare(EntryIntent(Plot(12, 5))));
        Assert.True(journal.Faulted);
        Assert.Empty(journal.Snapshot().Attempts);
        Assert.Equal(JournalWrite.Faulted, journal.Prepare(EntryIntent(Plot(12, 6))));
    }

    [Fact]
    public void EntrySlotSurvivesAPlotOrBuyerChange()
    {
        var journal = OpenJournal();
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
        var journal = OpenJournal();
        var first = EntryIntent(Plot(12, 5));
        journal.Prepare(first);

        Assert.Equal(JournalWrite.Written, journal.Reject(first.AttemptId, now));
        Assert.Equal(JournalWrite.Written, journal.Prepare(EntryIntent(Plot(12, 6))));
    }

    [Fact]
    public void NextCycleHasItsOwnSlot()
    {
        var journal = OpenJournal();
        journal.Prepare(EntryIntent(Plot(12, 5)));

        Assert.Equal(JournalWrite.Written, journal.Prepare(EntryIntent(Plot(12, 5)) with { Cycle = SecondCycle }));
    }

    [Fact]
    public void RestartTurnsPreparedAndDispatchedAttemptsIntoReconciliation()
    {
        var journal = OpenJournal();
        var prepared = EntryIntent(Plot(12, 5));
        var dispatched = EntryIntent(Plot(12, 6)) with { CharacterContentId = ContentId + 1 };
        journal.Prepare(prepared);
        journal.Prepare(dispatched);
        journal.RecordDispatch(dispatched.AttemptId, now);

        var restarted = ActionJournal.Open(path, now.AddHours(1));

        Assert.Equal(JournalOpenStatus.Opened, restarted.Status);
        Assert.Equal(2, restarted.RecoveredAttempts);
        var attempts = restarted.Journal!.Snapshot().Attempts;
        for (var index = 0; index < attempts.Length; index++)
        {
            Assert.Equal(AttemptState.NeedsReconciliation, attempts[index].State);
        }

        Assert.Equal(JournalWrite.SlotTaken, restarted.Journal.Prepare(EntryIntent(Plot(1, 1))));
        Assert.Equal(0, ActionJournal.Open(path, now.AddHours(2)).RecoveredAttempts);
    }

    [Fact]
    public void TruncatedLastLineIsDroppedAndTheRestRecovers()
    {
        var journal = OpenJournal();
        var intent = EntryIntent(Plot(12, 5));
        journal.Prepare(intent);
        File.AppendAllText(path, "{\"kind\":\"Attempt\",\"attempt\":{\"id\":\"");

        var reopened = ActionJournal.Open(path, now.AddMinutes(5));

        Assert.Equal(JournalOpenStatus.Opened, reopened.Status);
        Assert.True(reopened.TruncatedTail);
        Assert.Equal(intent.AttemptId, Assert.Single(reopened.Journal!.Snapshot().Attempts).AttemptId);
        Assert.EndsWith("\n", File.ReadAllText(path));
        Assert.False(ActionJournal.Open(path, now.AddMinutes(6)).TruncatedTail);
    }

    [Fact]
    public void CorruptCompleteLineFailsVisiblyInsteadOfLookingEmpty()
    {
        var journal = OpenJournal();
        journal.Prepare(EntryIntent(Plot(12, 5)));
        File.AppendAllText(path, "not json\n");

        var reopened = ActionJournal.Open(path, now);

        Assert.Equal(JournalOpenStatus.Corrupt, reopened.Status);
        Assert.Null(reopened.Journal);
    }

    [Fact]
    public void JournalFromANewerVersionIsNotTouched()
    {
        File.WriteAllText(path, "{\"kind\":\"Header\",\"schema\":99}\n");

        var reopened = ActionJournal.Open(path, now);

        Assert.Equal(JournalOpenStatus.NewerSchema, reopened.Status);
        Assert.Equal("{\"kind\":\"Header\",\"schema\":99}\n", File.ReadAllText(path));
    }

    [Fact]
    public void MigrationBacksUpTheOriginalBeforeUpgrading()
    {
        var journal = OpenJournal();
        var intent = EntryIntent(Plot(12, 5));
        journal.Prepare(intent);
        journal.Reject(intent.AttemptId, now);
        var original = File.ReadAllBytes(path);

        var migrated = ActionJournal.Open(path, now, 2, [new JournalMigration(1, AddGilBefore)]);

        Assert.Equal(JournalOpenStatus.Migrated, migrated.Status);
        Assert.Equal(original, File.ReadAllBytes(migrated.BackupPath!));
        Assert.Equal(42, Assert.Single(migrated.Journal!.Snapshot().Attempts).GilBefore);
        Assert.StartsWith("{\"kind\":\"Header\",\"schema\":2}", File.ReadAllText(path));
        Assert.Equal(JournalOpenStatus.Opened, ActionJournal.Open(path, now, 2, []).Status);
    }

    [Fact]
    public void MissingMigrationStepLeavesTheOriginalAndItsBackup()
    {
        var journal = OpenJournal();
        journal.Prepare(EntryIntent(Plot(12, 5)));
        var original = File.ReadAllBytes(path);

        var migrated = ActionJournal.Open(path, now, 3, [new JournalMigration(1, AddGilBefore)]);

        Assert.Equal(JournalOpenStatus.MigrationFailed, migrated.Status);
        Assert.Null(migrated.Journal);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.True(File.Exists(migrated.BackupPath));
    }

    [Fact]
    public void ConfirmationRecordsTheReceiptOnceAndOpensItsSettlement()
    {
        var journal = OpenJournal();
        var intent = EntryIntent(Plot(12, 5));
        journal.Prepare(intent);
        journal.RecordDispatch(intent.AttemptId, now);
        var receipt = Receipt(intent);

        Assert.Equal(JournalWrite.Invalid, journal.Confirm(intent.AttemptId, receipt with { Plot = Plot(12, 6) }, now));
        Assert.Equal(JournalWrite.Written, journal.Confirm(intent.AttemptId, receipt, now));
        Assert.Equal(JournalWrite.AlreadyRecorded, journal.Confirm(intent.AttemptId, receipt, now));
        Assert.Equal(JournalWrite.IllegalTransition, journal.RecordDispatch(intent.AttemptId, now));

        Assert.True(journal.TryGetSettlement(receipt.ReceiptId, out var settlement));
        Assert.Equal(SettlementState.Entered, settlement.State);
        var reopened = ActionJournal.Open(path, now).Journal!.Snapshot();
        Assert.Equal(receipt.ReceiptId, Assert.Single(reopened.Receipts).ReceiptId);
        Assert.Equal(AttemptState.Confirmed, Assert.Single(reopened.Attempts).State);
    }

    [Fact]
    public void RefundAttemptIsKeyedByItsReceipt()
    {
        var journal = OpenJournal();
        var receipt = ConfirmedEntry(journal, Plot(12, 5));
        var refund = RefundIntent(receipt);

        Assert.Equal(JournalWrite.Written, journal.Prepare(refund));
        Assert.Equal(JournalWrite.SlotTaken, journal.Prepare(RefundIntent(receipt)));
        Assert.Throws<ArgumentException>(() => OperationKey.ForSettlement(ActionKind.Entry, receipt.ReceiptId));
    }

    [Fact]
    public void IllegalSettlementTransitionsAreRefused()
    {
        var journal = OpenJournal();
        var receipt = ConfirmedEntry(journal, Plot(12, 5));

        Assert.Equal(JournalWrite.IllegalTransition, journal.AdvanceSettlement(receipt.ReceiptId, SettlementState.Refunded, Deposit, now));
        Assert.Equal(JournalWrite.Written, journal.AdvanceSettlement(receipt.ReceiptId, SettlementState.ResultsPending, null, now));
        Assert.Equal(JournalWrite.UnknownRecord, journal.AdvanceSettlement(Guid.NewGuid(), SettlementState.ResultsPending, null, now));
    }

    [Fact]
    public void CompactionKeepsUnsettledReceiptsAndPrunesOldSettledOnes()
    {
        var journal = OpenJournal();
        var refunded = ConfirmedEntry(journal, Plot(12, 5));
        var unsettled = ConfirmedEntry(journal, Plot(12, 6), ContentId + 1);
        Settle(journal, refunded.ReceiptId);

        Assert.Equal(JournalWrite.Written, journal.Compact(now.AddDays(200), out var pruned));

        Assert.Equal(1, pruned);
        var reopened = ActionJournal.Open(path, now.AddDays(200)).Journal!.Snapshot();
        Assert.Equal(unsettled.ReceiptId, Assert.Single(reopened.Receipts).ReceiptId);
        Assert.Equal(unsettled.ReceiptId, Assert.Single(reopened.Attempts).ReceiptId);
        Assert.False(File.Exists(path + ".tmp"));
    }

    private ActionJournal OpenJournal()
    {
        var opened = ActionJournal.Open(path, now);
        Assert.Equal(JournalOpenStatus.Created, opened.Status);
        return opened.Journal!;
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

    private static ActionAttempt RefundIntent(EntryReceipt receipt) => new()
    {
        AttemptId = Guid.NewGuid(),
        Kind = ActionKind.Refund,
        RunId = Guid.NewGuid(),
        CharacterContentId = receipt.CharacterContentId,
        HomeWorldId = receipt.HomeWorldId,
        Cycle = receipt.Cycle,
        Plot = receipt.Plot,
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

    private static void Settle(ActionJournal journal, Guid receiptId)
    {
        SettlementState[] steps =
        [
            SettlementState.ResultsPending,
            SettlementState.InspectingResult,
            SettlementState.LostRefundDue,
            SettlementState.RefundPending,
            SettlementState.Refunded,
        ];
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
