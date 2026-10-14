using System.Collections.Immutable;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Integration.Model;
using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Settlement.Model;
using static AutoHouseLottery.Tests.HousingFixtures;
using static AutoHouseLottery.Tests.Integration.IntegrationFixtures;

namespace AutoHouseLottery.Tests.Settlement;

public sealed class OutstandingAndManualSettlementTests
{
    private const ushort HomeWorldShort = (ushort)HomeWorld;

    [Fact]
    public void AKnownRefundIsCollectedFirstOnlyWithAutomaticRefunds()
    {
        Assert.Equal(OutstandingStep.CollectFirst, OutstandingPlan.Decide(OutstandingKind.RefundDue, automaticRefunds: true));
        Assert.Equal(OutstandingStep.StopCollectRefundFirst, OutstandingPlan.Decide(OutstandingKind.RefundDue, automaticRefunds: false));
        Assert.Equal(OutstandingStep.Proceed, OutstandingPlan.Decide(OutstandingKind.None, automaticRefunds: false));
    }

    [Fact]
    public void AResultNobodyReadIsVisitedWithAutomaticRefundsAndOtherwiseLeftToTheGame()
    {
        Assert.Equal(OutstandingStep.CollectFirst, OutstandingPlan.Decide(OutstandingKind.ResultUnchecked, automaticRefunds: true));
        Assert.Equal(OutstandingStep.Proceed, OutstandingPlan.Decide(OutstandingKind.ResultUnchecked, automaticRefunds: false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnUnclaimedWinAlwaysStopsForThePlayer(bool automaticRefunds)
    {
        Assert.Equal(OutstandingStep.StopUnclaimedWin, OutstandingPlan.Decide(OutstandingKind.UnclaimedWin, automaticRefunds));
        Assert.Equal(OutstandingStep.StopUnclaimedWin, OutstandingPlan.AfterCollection(OutstandingKind.UnclaimedWin));
    }

    [Fact]
    public void AfterTheVisitOnlyWhatIsStillKnownToWaitStopsTheRun()
    {
        Assert.Equal(OutstandingStep.Proceed, OutstandingPlan.AfterCollection(OutstandingKind.None));
        Assert.Equal(OutstandingStep.Proceed, OutstandingPlan.AfterCollection(OutstandingKind.ResultUnchecked));
        Assert.Equal(OutstandingStep.StopCollectRefundFirst, OutstandingPlan.AfterCollection(OutstandingKind.RefundDue));
    }

    [Fact]
    public void AnEntryNothingHereCanSettleNeverBlocksANewEntry()
    {
        var collectedByHand = Receipt(Plot(12, 5), FirstCycle);
        var neverRead = Receipt(Plot(12, 6), FirstCycle);
        var unreadable = Receipt(Plot(12, 7), FirstCycle);
        var won = Receipt(Plot(12, 8), FirstCycle);
        var sighted = new HashSet<Guid> { collectedByHand.ReceiptId, won.ReceiptId };
        bool LeftToTheGame(EntryReceipt receipt, EntrySettlement settlement)
            => PendingRules.LeftToTheGame(settlement.State, sighted.Contains(receipt.ReceiptId), receipt.Plot is not null);

        var onlyPending = Journal([], (collectedByHand, SettlementState.LostRefundDue), (unreadable, SettlementState.OutcomeUnknown));
        Assert.Equal(OutstandingKind.None, OutstandingRules.Find(onlyPending, ContentId, SecondCycle, LeftToTheGame).Kind);

        var unread = Journal([], (collectedByHand, SettlementState.LostRefundDue), (neverRead, SettlementState.Entered));
        Assert.Equal(new OutstandingEntry(OutstandingKind.ResultUnchecked, neverRead.ReceiptId), OutstandingRules.Find(unread, ContentId, SecondCycle, LeftToTheGame));

        var win = Journal([], (won, SettlementState.WonUnclaimed));
        Assert.Equal(OutstandingKind.UnclaimedWin, OutstandingRules.Find(win, ContentId, SecondCycle, LeftToTheGame).Kind);
    }

    [Fact]
    public void AnEntryIsLeftToTheGameOnlyWhenNothingHereCanSettleIt()
    {
        Assert.True(PendingRules.LeftToTheGame(SettlementState.OutcomeUnknown, noResultSeen: false, plotKnown: true));
        Assert.True(PendingRules.LeftToTheGame(SettlementState.LostRefundDue, noResultSeen: true, plotKnown: true));
        Assert.True(PendingRules.LeftToTheGame(SettlementState.Entered, noResultSeen: false, plotKnown: false));
        Assert.False(PendingRules.LeftToTheGame(SettlementState.Entered, noResultSeen: false, plotKnown: true));
        Assert.False(PendingRules.LeftToTheGame(SettlementState.LostRefundDue, noResultSeen: false, plotKnown: true));
    }

    [Fact]
    public void ThePlayerMayMarkAnEntrySettledInGameOnlyOnceItsResultsAreOut()
    {
        Assert.True(ManualSettlement.CanMarkSettledInGame(SettlementState.LostRefundDue, resultsArrived: true, attemptOpen: false));
        Assert.True(ManualSettlement.CanMarkSettledInGame(SettlementState.ForfeitRefundDue, resultsArrived: true, attemptOpen: false));
        Assert.True(ManualSettlement.CanMarkSettledInGame(SettlementState.OutcomeUnknown, resultsArrived: true, attemptOpen: false));
        Assert.True(ManualSettlement.CanMarkSettledInGame(SettlementState.Entered, resultsArrived: true, attemptOpen: false));
        Assert.False(ManualSettlement.CanMarkSettledInGame(SettlementState.Entered, resultsArrived: false, attemptOpen: false));
        Assert.False(ManualSettlement.CanMarkSettledInGame(SettlementState.LostRefundDue, resultsArrived: true, attemptOpen: true));
        Assert.False(ManualSettlement.CanMarkSettledInGame(SettlementState.WonUnclaimed, resultsArrived: true, attemptOpen: false));
        Assert.False(ManualSettlement.CanMarkSettledInGame(SettlementState.RefundPending, resultsArrived: true, attemptOpen: false));
        Assert.False(ManualSettlement.CanMarkSettledInGame(SettlementState.Refunded, resultsArrived: true, attemptOpen: false));
    }

    [Fact]
    public void ARefusalWith6100ReopensOnlyWhatWasSettledWithoutAResult()
    {
        var byAbsence = Receipt(Plot(12, 5), FirstCycle);
        var byResult = Receipt(Plot(12, 6), FirstCycle);
        var otherCharacter = Receipt(Plot(12, 7), FirstCycle) with { CharacterContentId = ContentId + 1 };
        var journal = Journal([], (byAbsence, SettlementState.Refunded), (byResult, SettlementState.Refunded), (otherCharacter, SettlementState.Refunded));
        journal = journal with { Settlements = SettledByAbsence(journal.Settlements, byAbsence.ReceiptId, otherCharacter.ReceiptId) };

        Assert.Equal(byAbsence.ReceiptId, Assert.Single(ManualSettlement.ReopenedBy6100(journal, ContentId)));
    }

    [Theory]
    [InlineData("Plot 35, 12 Ward, Mist (Small)", 12, 35)]
    [InlineData("Mist 12 35", 12, 35)]
    [InlineData("Mist, Ward 7, Plot 7", 7, 7)]
    public void AnAddressWithOnlyOneValidOrderNamesItsPlot(string text, int ward, int plot)
    {
        Assert.Equal(Plot(ward, plot), ImportedAddress.Resolve(text, GameRegion.Global, HomeWorld, MistOnly));
    }

    [Fact]
    public void AnAddressValidInBothOrdersIsNeverGuessed()
    {
        Assert.Null(ImportedAddress.Resolve("Plot 5, 12 Ward, Mist (Small)", GameRegion.Global, HomeWorld, MistOnly));
        Assert.Null(ImportedAddress.Resolve("Mist, Ward 12, Plot 5", GameRegion.Global, HomeWorld, MistOnly));
    }

    [Fact]
    public void AnImportedPlotIsKeptOnlyOnceAResultRowOrTheEstateListProvesIt()
    {
        Assert.True(SettlementDecisions.ProvesPlot(ResultKind.Lost, ownsPlot: false));
        Assert.True(SettlementDecisions.ProvesPlot(ResultKind.Won, ownsPlot: false));
        Assert.True(SettlementDecisions.ProvesPlot(ResultKind.Expired, ownsPlot: false));
        Assert.True(SettlementDecisions.ProvesPlot(ResultKind.NotShown, ownsPlot: true));
        Assert.False(SettlementDecisions.ProvesPlot(ResultKind.NotShown, ownsPlot: false));
        Assert.False(SettlementDecisions.ProvesPlot(ResultKind.Unrecognized, ownsPlot: false));
    }

    [Theory]
    [InlineData("Plot 5, 12 Ward")]
    [InlineData("Mist, Ward 12")]
    [InlineData("Mist, Ward 12, Plot 5, Room 3")]
    [InlineData("Mist, Ward 31, Plot 45")]
    [InlineData("Mist, Ward 61, Plot 5")]
    [InlineData("Mist, Ward 1200000, Plot 5")]
    public void AnAddressThatIsNotCertainNamesNoPlot(string text)
    {
        Assert.Null(ImportedAddress.Resolve(text, GameRegion.Global, HomeWorld, MistOnly));
    }

    [Fact]
    public void TwoDistrictNamesInOneAddressNameNoPlot()
    {
        static string? BothNamed(uint territory) => territory switch
        {
            HousingDistricts.Mist => "Mist",
            HousingDistricts.Goblet => "Goblet",
            _ => null,
        };

        Assert.Null(ImportedAddress.Resolve("Mist Goblet, Ward 12, Plot 35", GameRegion.Global, HomeWorld, BothNamed));
    }

    [Fact]
    public void AWinClaimedInGameSettlesFromTheEstateList()
    {
        var won = Receipt(Plot(12, 5), FirstCycle);
        var lost = Receipt(Plot(12, 6), FirstCycle);
        var journal = Journal([], (won, SettlementState.WonUnclaimed), (lost, SettlementState.LostRefundDue));

        var fix = Assert.Single(ManualSettlement.FromEstates(journal, ContentId, Estate(12, 5), null));

        Assert.Equal(new SettlementFix(won.ReceiptId, SettlementState.Claimed, null), fix);
        Assert.Empty(ManualSettlement.FromEstates(journal, ContentId, Estate(12, 6), null));
        Assert.Empty(ManualSettlement.FromEstates(journal, ContentId + 1, Estate(12, 5), null));
    }

    [Fact]
    public void AnOwnClaimInQuestionIsConfirmedByTheEstateList()
    {
        var won = Receipt(Plot(12, 5), FirstCycle);
        var claim = Attempt(Plot(12, 5), AttemptState.NeedsReconciliation) with { Kind = ActionKind.Claim, ReceiptId = won.ReceiptId };
        var journal = Journal([claim], (won, SettlementState.NeedsReconciliation));

        Assert.Equal(new SettlementFix(won.ReceiptId, SettlementState.Claimed, claim.AttemptId), Assert.Single(ManualSettlement.FromEstates(journal, ContentId, Estate(12, 5), null)));
    }

    [Fact]
    public void ARefundCollectedInGameSettlesFromAValidatedReadingInItsOwnResultsPeriod()
    {
        var lost = Receipt(Plot(12, 5), FirstCycle);
        var unknown = Receipt(Plot(12, 6), FirstCycle) with { CharacterContentId = ContentId };
        var journal = Journal([], (lost, SettlementState.LostRefundDue), (unknown, SettlementState.Entered));
        var none = Reading(LotterySnapshotState.LoadedNone);

        var fix = Assert.Single(ManualSettlement.FromReading(journal, none, ResultsPhase(FirstCycle)));

        Assert.Equal(new SettlementFix(lost.ReceiptId, SettlementState.Refunded, null), fix);
    }

    [Fact]
    public void NoEntryInAReadingThatCannotProveItSettlesNothing()
    {
        var lost = Receipt(Plot(12, 5), FirstCycle);
        var journal = Journal([], (lost, SettlementState.LostRefundDue));

        Assert.Empty(ManualSettlement.FromReading(journal, Reading(LotterySnapshotState.Unloaded), ResultsPhase(FirstCycle)));
        Assert.Empty(ManualSettlement.FromReading(journal, Reading(LotterySnapshotState.LoadedNone) with { Source = LotterySource.TimersText }, ResultsPhase(FirstCycle)));
        Assert.Empty(ManualSettlement.FromReading(journal, Reading(LotterySnapshotState.LoadedNone), ResultsPhase(SecondCycle)));
        Assert.Empty(ManualSettlement.FromReading(journal, Reading(LotterySnapshotState.LoadedNone), VerifiedEntryPhase()));
    }

    [Fact]
    public void AnOwnRefundAlreadyInQuestionIsConfirmedByAValidatedReading()
    {
        var lost = Receipt(Plot(12, 5), FirstCycle);
        var refund = Attempt(Plot(12, 5), AttemptState.NeedsReconciliation) with { Kind = ActionKind.Refund, ReceiptId = lost.ReceiptId };
        var journal = Journal([refund], (lost, SettlementState.NeedsReconciliation));

        var fix = Assert.Single(ManualSettlement.FromReading(journal, Reading(LotterySnapshotState.LoadedNone), ResultsPhase(FirstCycle)));

        Assert.Equal(new SettlementFix(lost.ReceiptId, SettlementState.Refunded, refund.AttemptId), fix);
    }

    [Theory]
    [InlineData((byte)SettlementState.RefundPending)]
    [InlineData((byte)SettlementState.LostRefundDue)]
    [InlineData((byte)SettlementState.ForfeitRefundDue)]
    public void AnOwnRefundLeftInQuestionByACrashIsConfirmedByAValidatedReading(byte recorded)
    {
        var lost = Receipt(Plot(12, 5), FirstCycle);
        var refund = Attempt(Plot(12, 5), AttemptState.NeedsReconciliation) with { Kind = ActionKind.Refund, ReceiptId = lost.ReceiptId };
        var journal = Journal([refund], (lost, (SettlementState)recorded));

        var fix = Assert.Single(ManualSettlement.FromReading(journal, Reading(LotterySnapshotState.LoadedNone), ResultsPhase(FirstCycle)));

        Assert.Equal(new SettlementFix(lost.ReceiptId, SettlementState.Refunded, refund.AttemptId), fix);
    }

    [Theory]
    [InlineData((byte)ActionKind.Refund, (byte)SettlementState.RefundPending, (byte)SettlementState.Refunded)]
    [InlineData((byte)ActionKind.Refund, (byte)SettlementState.NeedsReconciliation, (byte)SettlementState.Refunded)]
    [InlineData((byte)ActionKind.Claim, (byte)SettlementState.ClaimPending, (byte)SettlementState.Claimed)]
    [InlineData((byte)ActionKind.Claim, (byte)SettlementState.NeedsReconciliation, (byte)SettlementState.Claimed)]
    public void AConfirmedPressWhoseReceiptWasLeftBehindSettlesTheReceipt(byte kind, byte recorded, byte expected)
    {
        var entry = Receipt(Plot(12, 5), FirstCycle);
        var press = Attempt(Plot(12, 5), AttemptState.Confirmed) with { Kind = (ActionKind)kind, ReceiptId = entry.ReceiptId };
        var journal = Journal([press], (entry, (SettlementState)recorded));

        Assert.Equal(new SettlementFix(entry.ReceiptId, (SettlementState)expected, null), Assert.Single(ManualSettlement.FromOwnConfirmations(journal, ContentId)));
        Assert.Empty(ManualSettlement.FromOwnConfirmations(journal, ContentId + 1));
    }

    [Theory]
    [InlineData((byte)ActionKind.Refund)]
    [InlineData((byte)ActionKind.Claim)]
    public void AReceiptReopenedAfterItsConfirmedPressIsNotSettledAgain(byte kind)
    {
        var reopened = Receipt(Plot(12, 5), FirstCycle);
        var press = Attempt(Plot(12, 5), AttemptState.Confirmed) with { Kind = (ActionKind)kind, ReceiptId = reopened.ReceiptId };
        var journal = Journal([press], (reopened, SettlementState.OutcomeUnknown));

        Assert.Empty(ManualSettlement.FromOwnConfirmations(journal, ContentId));
    }

    [Fact]
    public void OnlyAConfirmedPressOnAnOpenReceiptIsRepaired()
    {
        var settled = Receipt(Plot(12, 5), FirstCycle);
        var questioned = Receipt(Plot(12, 6), FirstCycle);
        var done = Attempt(Plot(12, 5), AttemptState.Confirmed) with { Kind = ActionKind.Refund, ReceiptId = settled.ReceiptId };
        var open = Attempt(Plot(12, 6), AttemptState.NeedsReconciliation) with { Kind = ActionKind.Refund, ReceiptId = questioned.ReceiptId };
        var journal = Journal([done, open], (settled, SettlementState.Refunded), (questioned, SettlementState.RefundPending));

        Assert.Empty(ManualSettlement.FromOwnConfirmations(journal, ContentId));
    }

    [Theory]
    [InlineData((byte)AttemptState.Prepared)]
    [InlineData((byte)AttemptState.Dispatched)]
    public void AReadingNeverRacesTheVisitThatIsConfirmingItsOwnRefund(byte state)
    {
        var lost = Receipt(Plot(12, 5), FirstCycle);
        var refund = Attempt(Plot(12, 5), (AttemptState)state) with { Kind = ActionKind.Refund, ReceiptId = lost.ReceiptId };
        var journal = Journal([refund], (lost, SettlementState.RefundPending));

        Assert.Empty(ManualSettlement.FromReading(journal, Reading(LotterySnapshotState.LoadedNone), ResultsPhase(FirstCycle)));
    }

    private static ImmutableArray<EntrySettlement> SettledByAbsence(ImmutableArray<EntrySettlement> settlements, params Guid[] receiptIds)
    {
        var marked = settlements.ToBuilder();
        for (var index = 0; index < marked.Count; index++)
        {
            if (Array.IndexOf(receiptIds, marked[index].ReceiptId) >= 0)
            {
                marked[index] = marked[index] with { SettledByAbsence = true };
            }
        }

        return marked.ToImmutable();
    }

    private static EstateAddress Estate(int ward, int plot) => new(HousingDistricts.Mist, HomeWorldShort, ward - 1, plot - 1, false, 1);
}
