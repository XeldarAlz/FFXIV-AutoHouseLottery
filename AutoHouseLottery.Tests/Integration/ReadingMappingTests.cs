using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Integration.Model;
using AutoHouseLottery.Core.Persistence.Model;
using static AutoHouseLottery.Tests.HousingFixtures;
using static AutoHouseLottery.Tests.Integration.IntegrationFixtures;
using GameLotteryPhase = AutoHouseLottery.Core.Game.Model.LotteryPhase;

namespace AutoHouseLottery.Tests.Integration;

public sealed class ReadingMappingTests
{
    [Fact]
    public void ProfileKeepsWhatTheGameDidNotShowUnknown()
    {
        var profile = CharacterProfileMapping.From(Context(gil: null), GameRegion.Global, null, OutstandingSettlement.Unknown, default);

        Assert.Null(profile.ObservedGil);
        Assert.Null(profile.GilObservedUtc);
        Assert.Null(profile.FreeCompanyId);
        Assert.Null(profile.PrivateBuyerQualified);
        Assert.Null(profile.FreeCompanyBuyerQualified);
        Assert.Null(profile.AccountOwnsLandOnHomeWorld);
    }

    [Fact]
    public void OwnHouseOnTheHomeWorldIsTheOnlyProofOfLandOnTheAccount()
    {
        var house = new EstateAddress(HousingDistricts.Mist, (ushort)HomeWorld, 3, 7, false, 1);

        Assert.True(CharacterProfileMapping.OwnsLandOnHomeWorld(Context(personalEstate: house)));
        Assert.Null(CharacterProfileMapping.OwnsLandOnHomeWorld(Context(personalEstate: house with { WorldId = (ushort)OtherWorld })));
        Assert.Null(CharacterProfileMapping.OwnsLandOnHomeWorld(Context()));
    }

    [Fact]
    public void JournalSlotOutranksTheReading()
    {
        var pending = Journal([Attempt(Plot(12, 5), AttemptState.NeedsReconciliation)]);
        var journal = ParticipationRules.FromJournal(pending, ContentId, HomeWorld, FirstCycle);
        var reading = ParticipationRules.FromReading(Reading(LotterySnapshotState.LoadedNone), VerifiedEntryPhase(), GameRegion.Global, HomeWorld);

        var participation = ParticipationRules.Resolve(FirstCycle, journal, reading);

        Assert.Equal(EntrySlotState.Unresolved, participation.State);
        Assert.Equal(Plot(12, 5), participation.EnteredPlot);
    }

    [Fact]
    public void ImportedReceiptHoldsTheSlotEvenWithoutAPlot()
    {
        var snapshot = Journal([], (Receipt(null, FirstCycle, ReceiptEvidence.ImportedFromGame), SettlementState.Entered));

        var journal = ParticipationRules.FromJournal(snapshot, ContentId, HomeWorld, FirstCycle);

        Assert.Equal(EntrySlotState.Entered, journal.State);
        Assert.Null(journal.Plot);
        Assert.Equal(EntrySlotState.Unknown, ParticipationRules.FromJournal(snapshot, ContentId, HomeWorld, SecondCycle).State);
    }

    [Fact]
    public void OnlyTheValidatedRecordCanOpenTheSlot()
    {
        var position = VerifiedEntryPhase();

        Assert.Equal(EntrySlotState.Open, ParticipationRules.FromReading(Reading(LotterySnapshotState.LoadedNone), position, GameRegion.Global, HomeWorld).State);
        Assert.Equal(EntrySlotState.Unknown, ParticipationRules.FromReading(Reading(LotterySnapshotState.Unloaded), position, GameRegion.Global, HomeWorld).State);
        Assert.Equal(EntrySlotState.Unknown, ParticipationRules.FromReading(Reading(LotterySnapshotState.Invalid), position, GameRegion.Global, HomeWorld).State);
    }

    [Fact]
    public void EntryCountsForTheCycleOnlyInTheCyclesOwnPhase()
    {
        var entryPhase = VerifiedEntryPhase();

        var current = ParticipationRules.FromReading(RecordReading(12, 5), entryPhase, GameRegion.Global, HomeWorld);
        Assert.Equal(EntrySlotState.Entered, current.State);
        Assert.Equal(Plot(12, 5), current.Plot);

        Assert.Equal(EntrySlotState.Unknown, ParticipationRules.FromReading(RecordReading(12, 5, GameLotteryPhase.ResultsPeriod), entryPhase, GameRegion.Global, HomeWorld).State);
        Assert.Equal(EntrySlotState.Unknown, ParticipationRules.FromReading(TextReading("Plot 5, 12 Ward, Mist", phase: null), entryPhase, GameRegion.Global, HomeWorld).State);
    }

    [Fact]
    public void UnclaimedWinOutranksARefundAndAnUncheckedEarlierEntry()
    {
        var snapshot = Journal(
            [],
            (Receipt(Plot(1, 1), FirstCycle), SettlementState.Entered),
            (Receipt(Plot(2, 2), FirstCycle), SettlementState.LostRefundDue),
            (Receipt(Plot(3, 3), FirstCycle), SettlementState.WonUnclaimed));

        Assert.Equal(OutstandingKind.UnclaimedWin, OutstandingRules.Find(snapshot, ContentId, SecondCycle).Kind);
    }

    [Fact]
    public void EarlierEntryNobodyCheckedHoldsADepositButThisCyclesEntryDoesNot()
    {
        var snapshot = Journal([], (Receipt(Plot(1, 1), FirstCycle), SettlementState.Entered));

        Assert.Equal(OutstandingKind.ResultUnchecked, OutstandingRules.Find(snapshot, ContentId, SecondCycle).Kind);
        Assert.Equal(OutstandingKind.None, OutstandingRules.Find(snapshot, ContentId, FirstCycle).Kind);
        Assert.Equal(OutstandingKind.None, OutstandingRules.Find(Journal([], (Receipt(Plot(1, 1), FirstCycle), SettlementState.Refunded)), ContentId, SecondCycle).Kind);
    }

    [Fact]
    public void NothingKnownLeavesTheOutstandingQuestionToTheGame()
    {
        Assert.Equal(OutstandingSettlement.Unknown, OutstandingRules.ToSettlement(OutstandingKind.None));
        Assert.Equal(OutstandingSettlement.RefundDue, OutstandingRules.ToSettlement(OutstandingKind.ResultUnchecked));
        Assert.Equal(OutstandingSettlement.UnclaimedWin, OutstandingRules.ToSettlement(OutstandingKind.UnclaimedWin));
    }

    [Fact]
    public void ImportKeepsTheDepositAndEntryTimeUnknown()
    {
        var receipt = ImportedEntries.From(Guid.NewGuid(), ContentId, HomeWorld, RecordReading(12, 5), VerifiedEntryPhase());

        Assert.NotNull(receipt);
        Assert.Equal(Plot(12, 5), receipt.Plot);
        Assert.Equal(BuyerType.Private, receipt.Buyer);
        Assert.Equal((int)LotteryNumber, receipt.EntryNumber);
        Assert.Null(receipt.Deposit);
        Assert.Null(receipt.EnteredUtc);
        Assert.Equal(ReceiptEvidence.ImportedFromGame, receipt.Evidence);
    }

    [Fact]
    public void ImportFromTheTimersTextLeavesThePlotAndBuyerUnknown()
    {
        const string shown = "Plot 5, 12 Ward, Mist (Small)";
        var receipt = ImportedEntries.From(Guid.NewGuid(), ContentId, HomeWorld, TextReading(shown), VerifiedEntryPhase());

        Assert.NotNull(receipt);
        Assert.Null(receipt.Plot);
        Assert.Equal(BuyerType.Unknown, receipt.Buyer);
        Assert.Equal(shown, receipt.AddressText);
    }

    [Fact]
    public void NoImportFromAnotherPhaseOrAnotherCharacter()
    {
        Assert.Null(ImportedEntries.From(Guid.NewGuid(), ContentId, HomeWorld, RecordReading(12, 5, GameLotteryPhase.ResultsPeriod), VerifiedEntryPhase()));
        Assert.Null(ImportedEntries.From(Guid.NewGuid(), ContentId, HomeWorld, RecordReading(12, 5, contentId: ContentId + 1), VerifiedEntryPhase()));
        Assert.Null(ImportedEntries.From(Guid.NewGuid(), ContentId, HomeWorld, Reading(LotterySnapshotState.Unloaded), VerifiedEntryPhase()));
    }

    [Fact]
    public void ReadingNamesThePlotFieldByFieldOrByItsText()
    {
        Assert.True(ReadingAddress.Names(RecordReading(12, 5).Entry!.Value, Plot(12, 5), MistOnly));
        Assert.False(ReadingAddress.Names(RecordReading(12, 6).Entry!.Value, Plot(12, 5), MistOnly));
        Assert.True(ReadingAddress.Names(TextReading("Plot 5, 12 Ward, Mist (Small)").Entry!.Value, Plot(12, 5), MistOnly));
        Assert.False(ReadingAddress.Names(TextReading("Plot 9, 12 Ward, Mist (Small)").Entry!.Value, Plot(12, 5), MistOnly));
        Assert.Null(ReadingAddress.Names(TextReading(null).Entry!.Value, Plot(12, 5), MistOnly));
        Assert.Null(ReadingAddress.Names(TextReading("Plot 5, 12 Ward, Mist").Entry!.Value, Plot(12, 5), _ => null));
    }
}
