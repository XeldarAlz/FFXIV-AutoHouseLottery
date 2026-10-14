using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Settlement.Model;
using AutoHouseLottery.Core.Travel.Model;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests.Settlement;

public sealed class InspectionRulesTests
{
    private const long ForfeitRefund = Deposit / 2;

    [Theory]
    [InlineData((byte)HousingText.ResultWon, (byte)InspectionOutcome.WonUnclaimed)]
    [InlineData((byte)HousingText.ResultSoleEntrantWon, (byte)InspectionOutcome.WonUnclaimed)]
    [InlineData((byte)HousingText.ResultLost, (byte)InspectionOutcome.LostRefundDue)]
    [InlineData((byte)HousingText.ResultForfeited, (byte)InspectionOutcome.ForfeitRefundDue)]
    [InlineData((byte)HousingText.RefundExpired, (byte)InspectionOutcome.Expired)]
    public void EachResultRowHasItsOwnOutcome(byte row, byte expected)
    {
        var verdict = InspectionRules.Decide(Facts(ResultKinds.Of(true, (HousingText)row), offered: Deposit));

        Assert.Equal((InspectionOutcome)expected, verdict.Outcome);
        Assert.Equal(InspectionReason.None, verdict.Reason);
    }

    [Fact]
    public void OnlyARefundRowCarriesAnAmount()
    {
        Assert.Equal(Deposit, InspectionRules.Decide(Facts(ResultKind.Lost, offered: Deposit)).Refund);
        Assert.Equal(ForfeitRefund, InspectionRules.Decide(Facts(ResultKind.Forfeited, offered: ForfeitRefund)).Refund);
        Assert.Null(InspectionRules.Decide(Facts(ResultKind.Won, offered: Deposit)).Refund);
    }

    [Theory]
    [InlineData((byte)HousingText.EnterLotteryButton)]
    [InlineData((byte)HousingText.EntryConfirmation)]
    [InlineData((byte)HousingText.LotteryNumber)]
    public void AnyOtherDialogIsAnUnknownOutcome(byte row)
    {
        var verdict = InspectionRules.Decide(Facts(ResultKinds.Of(true, (HousingText)row)));
        var unmatched = InspectionRules.Decide(Facts(ResultKinds.Of(true, null)));

        Assert.Equal(InspectionReason.UnrecognizedDialog, unmatched.Reason);

        Assert.Equal(InspectionOutcome.OutcomeUnknown, verdict.Outcome);
        Assert.Equal(InspectionReason.UnrecognizedDialog, verdict.Reason);
    }

    [Theory]
    [InlineData((byte)ResultKind.Lost)]
    [InlineData((byte)ResultKind.Forfeited)]
    public void ARefundWhoseAmountCannotBeReadIsNotRecordedAsDue(byte kind)
    {
        var verdict = InspectionRules.Decide(Facts((ResultKind)kind, offered: null));

        Assert.Equal(InspectionOutcome.OutcomeUnknown, verdict.Outcome);
        Assert.Equal(InspectionReason.AmountUnreadable, verdict.Reason);
    }

    [Theory]
    [InlineData((byte)PlacardIdentityVerdict.WrongPlot, (byte)InspectionReason.WrongPlacard)]
    [InlineData((byte)PlacardIdentityVerdict.Unverified, (byte)InspectionReason.PlacardUnverified)]
    public void AResultReadAtAnUnconfirmedPlacardCountsForNothing(byte identity, byte reason)
    {
        var verdict = InspectionRules.Decide(Facts(ResultKind.Lost, offered: Deposit) with { Identity = (PlacardIdentityVerdict)identity });

        Assert.Equal(InspectionOutcome.OutcomeUnknown, verdict.Outcome);
        Assert.Equal((InspectionReason)reason, verdict.Reason);
    }

    [Fact]
    public void AWinNeverReadsAsALossOrTheOtherWayRound()
    {
        var lossAfterWin = InspectionRules.Decide(Facts(ResultKind.Lost, offered: Deposit, recorded: SettlementState.WonUnclaimed));
        var winAfterLoss = InspectionRules.Decide(Facts(ResultKind.Won, recorded: SettlementState.LostRefundDue, recordedRefund: RefundKind.Loss));
        var forfeitAfterLoss = InspectionRules.Decide(Facts(ResultKind.Forfeited, offered: ForfeitRefund, recorded: SettlementState.LostRefundDue, recordedRefund: RefundKind.Loss));
        var lossDuringClaim = InspectionRules.Decide(Facts(ResultKind.Lost, offered: Deposit, recorded: SettlementState.NeedsReconciliation, open: ActionKind.Claim));

        Assert.Equal(InspectionReason.ContradictsRecord, lossAfterWin.Reason);
        Assert.Equal(InspectionReason.ContradictsRecord, winAfterLoss.Reason);
        Assert.Equal(InspectionReason.ContradictsRecord, forfeitAfterLoss.Reason);
        Assert.Equal(InspectionReason.ContradictsRecord, lossDuringClaim.Reason);
    }

    [Fact]
    public void AnUnclaimedWinPastItsResultsPeriodBecomesAForfeitRefund()
    {
        var verdict = InspectionRules.Decide(Facts(ResultKind.Forfeited, offered: ForfeitRefund, recorded: SettlementState.WonUnclaimed));

        Assert.Equal(InspectionOutcome.ForfeitRefundDue, verdict.Outcome);
        Assert.Equal(ForfeitRefund, verdict.Refund);
    }

    [Fact]
    public void ARefundStillOfferedAfterOwnPressMeansItWasNotCollected()
    {
        var verdict = InspectionRules.Decide(Facts(ResultKind.Lost, offered: Deposit, recorded: SettlementState.NeedsReconciliation, recordedRefund: RefundKind.Loss, open: ActionKind.Refund));

        Assert.Equal(InspectionOutcome.LostRefundDue, verdict.Outcome);
    }

    [Fact]
    public void NoResultOfferedIsNeverReadAsALoss()
    {
        var verdict = InspectionRules.Decide(Facts(ResultKind.NotShown, offered: null, resultsOffered: false));

        Assert.NotEqual(InspectionOutcome.LostRefundDue, verdict.Outcome);
        Assert.Null(verdict.Refund);
    }

    [Fact]
    public void APressedButtonWithoutADialogOrAReadingStillShowingTheEntryDecidesNothing()
    {
        var buttonButNoDialog = InspectionRules.Decide(Facts(ResultKind.NotShown, offered: null, resultsOffered: true, readingShowsEntry: false));
        var entryStillShown = InspectionRules.Decide(Facts(ResultKind.NotShown, offered: null, resultsOffered: false, readingShowsEntry: true));
        var claimInQuestion = InspectionRules.Decide(Facts(ResultKind.NotShown, resultsOffered: false, recorded: SettlementState.NeedsReconciliation, open: ActionKind.Claim));

        Assert.Equal(InspectionReason.ResultNotShown, buttonButNoDialog.Reason);
        Assert.Equal(InspectionReason.NoResultOffered, entryStillShown.Reason);
        Assert.Equal(InspectionReason.NoResultOffered, claimInQuestion.Reason);
    }

    [Fact]
    public void AResultNoLongerOfferedWasSettledOutsideTheVisitForAnAmountNobodySaw()
    {
        var owed = InspectionRules.Decide(Facts(ResultKind.NotShown, resultsOffered: false, recorded: SettlementState.LostRefundDue, recordedRefund: RefundKind.Loss));
        var byOwnPress = InspectionRules.Decide(Facts(ResultKind.NotShown, resultsOffered: false, recorded: SettlementState.NeedsReconciliation, recordedRefund: RefundKind.Loss, open: ActionKind.Refund) with { RefundWindowPassed = true });
        var neverRead = InspectionRules.Decide(Facts(ResultKind.NotShown, resultsOffered: false, readingShowsEntry: false));
        var lapsed = InspectionRules.Decide(Facts(ResultKind.NotShown, resultsOffered: false) with { RefundWindowPassed = true });

        Assert.Equal(new InspectionVerdict(InspectionOutcome.Refunded, null, InspectionReason.None), owed);
        Assert.Equal(new InspectionVerdict(InspectionOutcome.Refunded, null, InspectionReason.None), byOwnPress);
        Assert.Equal(InspectionOutcome.Refunded, neverRead.Outcome);
        Assert.Equal(InspectionOutcome.Expired, lapsed.Outcome);
    }

    [Fact]
    public void AClaimMadeInGameSettlesTheWin()
    {
        var claimed = InspectionRules.Decide(Facts(ResultKind.NotShown, resultsOffered: false, recorded: SettlementState.WonUnclaimed) with { OwnsPlot = true });
        var neverRead = InspectionRules.Decide(Facts(ResultKind.NotShown, resultsOffered: false) with { OwnsPlot = true });
        var refundOnRecord = InspectionRules.Decide(Facts(ResultKind.NotShown, resultsOffered: false, recorded: SettlementState.LostRefundDue, recordedRefund: RefundKind.Loss) with { OwnsPlot = true });

        Assert.Equal(InspectionOutcome.Claimed, claimed.Outcome);
        Assert.Equal(InspectionOutcome.Claimed, neverRead.Outcome);
        Assert.Equal(InspectionOutcome.OutcomeUnknown, refundOnRecord.Outcome);
    }

    [Fact]
    public void AnyResultRowAtTheAttemptedPlotProvesTheEntryWasMade()
    {
        Assert.Equal(AttemptVerdict.Entered, InspectionRules.DecideAttempt(PlacardIdentityVerdict.Confirmed, true, ResultKind.Lost, ownsPlot: false));
        Assert.Equal(AttemptVerdict.Entered, InspectionRules.DecideAttempt(PlacardIdentityVerdict.Confirmed, true, ResultKind.SoleEntrantWon, ownsPlot: false));
        Assert.Equal(AttemptVerdict.Entered, InspectionRules.DecideAttempt(PlacardIdentityVerdict.Confirmed, true, ResultKind.Expired, ownsPlot: false));
        Assert.Equal(AttemptVerdict.Entered, InspectionRules.DecideAttempt(PlacardIdentityVerdict.Confirmed, false, ResultKind.NotShown, ownsPlot: true));
    }

    [Fact]
    public void OnlyAConfirmedPlacardWithoutAnyResultProvesTheEntryWasNotMade()
    {
        Assert.Equal(AttemptVerdict.NotEntered, InspectionRules.DecideAttempt(PlacardIdentityVerdict.Confirmed, false, ResultKind.NotShown, ownsPlot: false));
        Assert.Equal(AttemptVerdict.Unknown, InspectionRules.DecideAttempt(PlacardIdentityVerdict.Confirmed, true, ResultKind.NotShown, ownsPlot: false));
        Assert.Equal(AttemptVerdict.Unknown, InspectionRules.DecideAttempt(PlacardIdentityVerdict.Unverified, false, ResultKind.NotShown, ownsPlot: false));
        Assert.Equal(AttemptVerdict.Unknown, InspectionRules.DecideAttempt(PlacardIdentityVerdict.WrongPlot, true, ResultKind.Lost, ownsPlot: false));
        Assert.Equal(AttemptVerdict.Unknown, InspectionRules.DecideAttempt(PlacardIdentityVerdict.Confirmed, true, ResultKind.Unrecognized, ownsPlot: false));
    }

    private static InspectionFacts Facts(
        ResultKind result,
        long? offered = Deposit,
        SettlementState recorded = SettlementState.ResultsPending,
        RefundKind recordedRefund = RefundKind.None,
        ActionKind? open = null,
        bool resultsOffered = true,
        bool? readingShowsEntry = null)
        => new(PlacardIdentityVerdict.Confirmed, resultsOffered, result, offered, recorded, recordedRefund, open, readingShowsEntry, OwnsPlot: false, RefundWindowPassed: false);
}
