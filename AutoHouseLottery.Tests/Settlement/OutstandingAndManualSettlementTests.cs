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
    public void AnOutstandingRefundIsCollectedFirstOnlyWithAutomaticRefunds()
    {
        Assert.Equal(OutstandingStep.CollectFirst, OutstandingPlan.Decide(OutstandingKind.RefundDue, automaticRefunds: true));
        Assert.Equal(OutstandingStep.StopCollectRefundFirst, OutstandingPlan.Decide(OutstandingKind.RefundDue, automaticRefunds: false));
        Assert.Equal(OutstandingStep.CollectFirst, OutstandingPlan.Decide(OutstandingKind.ResultUnchecked, automaticRefunds: true));
        Assert.Equal(OutstandingStep.StopCheckEarlierResult, OutstandingPlan.Decide(OutstandingKind.ResultUnchecked, automaticRefunds: false));
        Assert.Equal(OutstandingStep.Proceed, OutstandingPlan.Decide(OutstandingKind.None, automaticRefunds: false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnUnclaimedWinAlwaysStopsForThePlayer(bool automaticRefunds)
    {
        Assert.Equal(OutstandingStep.StopUnclaimedWin, OutstandingPlan.Decide(OutstandingKind.UnclaimedWin, automaticRefunds));
        Assert.Equal(OutstandingStep.StopUnclaimedWin, OutstandingPlan.AfterCollection(SettlementResult.Classified, InspectionOutcome.WonUnclaimed));
    }

    [Fact]
    public void ADryRunCollectionCannotFreeTheSlot()
    {
        Assert.Equal(OutstandingStep.StopDryRunCollection, OutstandingPlan.AfterCollection(SettlementResult.DryRun, InspectionOutcome.LostRefundDue));
    }

    [Fact]
    public void ACollectedOrSettledEntryLetsTheRunGoOn()
    {
        Assert.Equal(OutstandingStep.Proceed, OutstandingPlan.AfterCollection(SettlementResult.Refunded, InspectionOutcome.LostRefundDue));
        Assert.Equal(OutstandingStep.Proceed, OutstandingPlan.AfterCollection(SettlementResult.RefundedAmountDiffers, InspectionOutcome.ForfeitRefundDue));
        Assert.Equal(OutstandingStep.Proceed, OutstandingPlan.AfterCollection(SettlementResult.Classified, InspectionOutcome.Refunded));
        Assert.Equal(OutstandingStep.Proceed, OutstandingPlan.AfterCollection(SettlementResult.Classified, InspectionOutcome.Expired));
    }

    [Fact]
    public void AnythingLessThanASettlementStopsTheRun()
    {
        Assert.Equal(OutstandingStep.StopCollectRefundFirst, OutstandingPlan.AfterCollection(SettlementResult.NeedsPlayer, InspectionOutcome.LostRefundDue));
        Assert.Equal(OutstandingStep.StopCheckEarlierResult, OutstandingPlan.AfterCollection(SettlementResult.NeedsReconciliation, null));
        Assert.Equal(OutstandingStep.StopCheckEarlierResult, OutstandingPlan.AfterCollection(SettlementResult.NeedsPlayer, InspectionOutcome.OutcomeUnknown));
        Assert.Equal(OutstandingStep.StopCheckEarlierResult, OutstandingPlan.AfterCollection(SettlementResult.Stopped, InspectionOutcome.Refunded));
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
    [InlineData((byte)AttemptState.Prepared)]
    [InlineData((byte)AttemptState.Dispatched)]
    public void AReadingNeverRacesTheVisitThatIsConfirmingItsOwnRefund(byte state)
    {
        var lost = Receipt(Plot(12, 5), FirstCycle);
        var refund = Attempt(Plot(12, 5), (AttemptState)state) with { Kind = ActionKind.Refund, ReceiptId = lost.ReceiptId };
        var journal = Journal([refund], (lost, SettlementState.RefundPending));

        Assert.Empty(ManualSettlement.FromReading(journal, Reading(LotterySnapshotState.LoadedNone), ResultsPhase(FirstCycle)));
    }

    private static EstateAddress Estate(int ward, int plot) => new(HousingDistricts.Mist, HomeWorldShort, ward - 1, plot - 1, false, 1);
}
