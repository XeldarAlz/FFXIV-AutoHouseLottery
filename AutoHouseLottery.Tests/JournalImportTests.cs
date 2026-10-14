using System.IO;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence;
using AutoHouseLottery.Core.Persistence.Model;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests;

public sealed class JournalImportTests : IDisposable
{
    private const ulong ContentId = 4_000_000_002;
    private const int LotteryNumber = 412;
    private const string ShownAddress = "Plot 5, 12 Ward, Mist (Small)";
    private static readonly DateTime now = ObservedAt;

    private readonly string directory = Path.Combine(Path.GetTempPath(), "ahl-import-" + Guid.NewGuid().ToString("N"));
    private readonly string path;
    private readonly List<ActionJournal> opened = [];

    public JournalImportTests()
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

        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void ImportedEntryTakesTheCycleSlotFromAnyLaterIntent()
    {
        var journal = Open();
        Assert.Equal(JournalWrite.Written, journal.ImportReceipt(Imported(null), now));

        Assert.Equal(JournalWrite.SlotTaken, journal.Prepare(Intent(Plot(12, 5))));
        Assert.Equal(JournalWrite.SlotTaken, journal.Prepare(Intent(Plot(3, 40)) with { Buyer = BuyerType.FreeCompany }));
        Assert.Equal(JournalWrite.Written, journal.Prepare(Intent(Plot(12, 5)) with { Cycle = SecondCycle }));
    }

    [Fact]
    public void ImportWithAnUnreadPlotSurvivesARestartWithItsUnknownsIntact()
    {
        var journal = Open();
        journal.ImportReceipt(Imported(null), now);
        journal.Dispose();
        opened.Remove(journal);

        var reopened = Open();
        var snapshot = reopened.Snapshot();
        var receipt = Assert.Single(snapshot.Receipts);
        Assert.Null(receipt.Plot);
        Assert.Null(receipt.Deposit);
        Assert.Null(receipt.EnteredUtc);
        Assert.Equal(ShownAddress, receipt.AddressText);
        Assert.Equal(LotteryNumber, receipt.EntryNumber);
        Assert.Empty(snapshot.OrphanedReceipts);
        Assert.Equal(SettlementState.Entered, Assert.Single(snapshot.Settlements).State);
    }

    [Fact]
    public void ImportIsRefusedWhileThePluginsOwnAttemptHoldsTheSlot()
    {
        var journal = Open();
        var intent = Intent(Plot(12, 5));
        journal.Prepare(intent);

        Assert.Equal(JournalWrite.SlotTaken, journal.ImportReceipt(Imported(Plot(12, 5)), now));

        journal.RequireReconciliation(intent.AttemptId, now);
        Assert.Equal(JournalWrite.SlotTaken, journal.ImportReceipt(Imported(Plot(12, 5)), now));
        Assert.Empty(journal.Snapshot().Receipts);
    }

    [Fact]
    public void SecondImportOfTheSameSlotIsAlreadyRecorded()
    {
        var journal = Open();
        journal.ImportReceipt(Imported(null), now);

        Assert.Equal(JournalWrite.AlreadyRecorded, journal.ImportReceipt(Imported(Plot(12, 5)), now));
        Assert.Single(journal.Snapshot().Receipts);
    }

    [Fact]
    public void ImportedReceiptCannotConfirmAnAttempt()
    {
        var journal = Open();
        var intent = Intent(Plot(12, 5));
        journal.Prepare(intent);
        journal.RecordDispatch(intent.AttemptId, now);

        var imported = Imported(Plot(12, 5)) with { Buyer = BuyerType.Private, Deposit = Deposit };
        Assert.Equal(JournalWrite.Invalid, journal.Confirm(intent.AttemptId, imported, now));
    }

    [Fact]
    public void ImportOfAPlotOffTheHomeWorldIsInvalid()
    {
        var journal = Open();

        Assert.Equal(JournalWrite.Invalid, journal.ImportReceipt(Imported(Plot(12, 5, OtherWorld)), now));
        Assert.Equal(JournalWrite.Invalid, journal.ImportReceipt(Imported(null) with { Evidence = ReceiptEvidence.NativeConfirmation }, now));
    }

    private ActionJournal Open()
    {
        var result = ActionJournal.Open(path, now);
        var journal = Assert.IsType<ActionJournal>(result.Journal);
        opened.Add(journal);
        return journal;
    }

    private static EntryReceipt Imported(PlotKey? plot) => new()
    {
        ReceiptId = Guid.NewGuid(),
        CharacterContentId = ContentId,
        HomeWorldId = HomeWorld,
        Cycle = FirstCycle,
        Plot = plot,
        Buyer = BuyerType.Unknown,
        Evidence = ReceiptEvidence.ImportedFromGame,
        EntryNumber = LotteryNumber,
        AddressText = ShownAddress,
    };

    private static ActionAttempt Intent(PlotKey plot) => new()
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
}
