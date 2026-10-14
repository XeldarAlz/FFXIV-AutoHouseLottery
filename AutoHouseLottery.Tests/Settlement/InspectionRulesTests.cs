using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Settlement.Model;
using AutoHouseLottery.Core.Travel.Model;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests.Settlement;

public sealed class InspectionRulesTests
{
    private const long ForfeitRefund = Deposit / 2;

    private static readonly PlacardEvidence strongPlacard = new(SaleRead: true, Unowned: true, AllLabelsKnown: true, LotteryButtonShown: true);

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
        Assert.False(verdict.ByAbsence);
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
        Assert.Equal(InspectionReason.UnrecognizedDialog, InspectionRules.Decide(Facts(ResultKinds.Of(true, (HousingText)row))).Reason);
        Assert.Equal(InspectionReason.UnrecognizedDialog, InspectionRules.Decide(Facts(ResultKinds.Of(true, null))).Reason);
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

    [Theory]
    [InlineData((byte)ResultKind.Lost)]
    [InlineData((byte)ResultKind.Forfeited)]
    [InlineData((byte)ResultKind.Expired)]
    [InlineData((byte)ResultKind.Won)]
    public void AnOwnedPlotNeverOverridesAnExplicitResultRow(byte kind)
    {
        var verdict = InspectionRules.Decide(Facts((ResultKind)kind, offered: Deposit) with { OwnsPlot = true });

        Assert.Equal(InspectionOutcome.OutcomeUnknown, verdict.Outcome);
        Assert.Equal(InspectionReason.ContradictsRecord, verdict.Reason);
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
    public void WhileTheAbsenceRuleIsUnvalidatedNoResultOfferedLeavesTheEntryPending()
    {
        var everything = Absence(validated: false) with { ReadingShowsNoEntry = true, SeenInAnotherSession = true, ClaimDeadlinePassed = true };

        Assert.False(SettlementCapabilities.AbsenceSettlementValidated);
        Assert.Equal(InspectionVerdict.Unknown(InspectionReason.NoResultOffered), InspectionRules.Decide(NoResult() with { Absence = everything }));
        Assert.Equal(InspectionVerdict.Unknown(InspectionReason.NoResultOffered),
            InspectionRules.Decide(NoResult(SettlementState.LostRefundDue, RefundKind.Loss) with { Absence = everything }));
    }

    [Fact]
    public void ThisCyclesAbsenceNeedsTheRecordToShowNoEntry()
    {
        var missingReading = Absence() with { CurrentCycle = true, ReadingShowsNoEntry = false };
        var proved = missingReading with { ReadingShowsNoEntry = true };

        Assert.Equal(InspectionReason.NoResultOffered, InspectionRules.Decide(NoResult() with { Absence = missingReading }).Reason);
        var settled = InspectionRules.Decide(NoResult() with { Absence = proved });
        Assert.Equal(InspectionOutcome.Refunded, settled.Outcome);
        Assert.True(settled.ByAbsence);
        Assert.Null(settled.Refund);
    }

    [Fact]
    public void AnEarlierCyclesAbsenceNeedsTwoSessions()
    {
        var once = Absence() with { CurrentCycle = false, SeenInAnotherSession = false };

        Assert.Equal(InspectionReason.NoResultOffered, InspectionRules.Decide(NoResult() with { Absence = once }).Reason);
        Assert.Equal(InspectionOutcome.Refunded, InspectionRules.Decide(NoResult() with { Absence = once with { SeenInAnotherSession = true } }).Outcome);
        Assert.Equal(InspectionOutcome.Expired, InspectionRules.Decide(NoResult() with { Absence = once with { SeenInAnotherSession = true }, RefundWindowPassed = true }).Outcome);
    }

    [Fact]
    public void AWinWithoutAResultIsNeverSettledBeforeItsClaimDeadline()
    {
        var beforeDeadline = Absence() with { SeenInAnotherSession = true, ReadingShowsNoEntry = true, ClaimDeadlinePassed = false };

        Assert.Equal(InspectionReason.NoResultOffered, InspectionRules.Decide(NoResult(SettlementState.WonUnclaimed) with { Absence = beforeDeadline }).Reason);
        Assert.Equal(InspectionOutcome.Refunded, InspectionRules.Decide(NoResult(SettlementState.WonUnclaimed) with { Absence = beforeDeadline with { ClaimDeadlinePassed = true } }).Outcome);
    }

    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    public void OnlyAFullyReadPlacardOfAnUnownedPlotWithKnownButtonsCounts(bool saleRead, bool unowned, bool allKnown, bool lotteryButton)
    {
        var weak = Absence() with { Placard = new PlacardEvidence(saleRead, unowned, allKnown, lotteryButton), SeenInAnotherSession = true, ReadingShowsNoEntry = true };

        Assert.Equal(InspectionReason.NoResultOffered, InspectionRules.Decide(NoResult() with { Absence = weak }).Reason);
        Assert.Equal(AttemptVerdict.Unknown, InspectionRules.DecideAttempt(PlacardIdentityVerdict.Confirmed, false, ResultKind.NotShown, false, weak));
    }

    [Fact]
    public void AClaimInQuestionOrAPressedButtonWithoutADialogDecidesNothing()
    {
        var strong = Absence() with { SeenInAnotherSession = true };

        Assert.Equal(InspectionReason.NoResultOffered, InspectionRules.Decide(NoResult(SettlementState.NeedsReconciliation) with { OpenAttempt = ActionKind.Claim, Absence = strong }).Reason);
        Assert.Equal(InspectionReason.ResultNotShown, InspectionRules.Decide(NoResult() with { ResultsOffered = true, Absence = strong }).Reason);
    }

    [Fact]
    public void AClaimMadeInGameSettlesTheWinButNeverARefundOnRecord()
    {
        var claimed = InspectionRules.Decide(NoResult(SettlementState.WonUnclaimed) with { OwnsPlot = true });
        var neverRead = InspectionRules.Decide(NoResult() with { OwnsPlot = true });
        var refundOnRecord = InspectionRules.Decide(NoResult(SettlementState.LostRefundDue, RefundKind.Loss) with { OwnsPlot = true });

        Assert.Equal(InspectionOutcome.Claimed, claimed.Outcome);
        Assert.Equal(InspectionOutcome.Claimed, neverRead.Outcome);
        Assert.Equal(InspectionReason.ContradictsRecord, refundOnRecord.Reason);
    }

    [Fact]
    public void AnyResultRowAtTheAttemptedPlotProvesTheEntryWasMade()
    {
        var none = Absence(validated: false);

        Assert.Equal(AttemptVerdict.Entered, InspectionRules.DecideAttempt(PlacardIdentityVerdict.Confirmed, true, ResultKind.Lost, ownsPlot: false, none));
        Assert.Equal(AttemptVerdict.Entered, InspectionRules.DecideAttempt(PlacardIdentityVerdict.Confirmed, true, ResultKind.SoleEntrantWon, ownsPlot: false, none));
        Assert.Equal(AttemptVerdict.Entered, InspectionRules.DecideAttempt(PlacardIdentityVerdict.Confirmed, true, ResultKind.Expired, ownsPlot: false, none));
        Assert.Equal(AttemptVerdict.Entered, InspectionRules.DecideAttempt(PlacardIdentityVerdict.Confirmed, false, ResultKind.NotShown, ownsPlot: true, none));
    }

    [Fact]
    public void OnlyTheValidatedAbsenceRuleProvesAnAttemptNeverEntered()
    {
        var strong = Absence() with { SeenInAnotherSession = true };

        Assert.Equal(AttemptVerdict.Unknown, InspectionRules.DecideAttempt(PlacardIdentityVerdict.Confirmed, false, ResultKind.NotShown, false, strong with { Validated = false }));
        Assert.Equal(AttemptVerdict.NotEntered, InspectionRules.DecideAttempt(PlacardIdentityVerdict.Confirmed, false, ResultKind.NotShown, false, strong));
        Assert.Equal(AttemptVerdict.Unknown, InspectionRules.DecideAttempt(PlacardIdentityVerdict.Confirmed, true, ResultKind.NotShown, false, strong));
        Assert.Equal(AttemptVerdict.Unknown, InspectionRules.DecideAttempt(PlacardIdentityVerdict.Unverified, false, ResultKind.NotShown, false, strong));
        Assert.Equal(AttemptVerdict.Unknown, InspectionRules.DecideAttempt(PlacardIdentityVerdict.Confirmed, true, ResultKind.Unrecognized, false, strong));
    }

    private static AbsenceEvidence Absence(bool validated = true)
        => new(validated, strongPlacard, CurrentCycle: false, ReadingShowsNoEntry: false, SeenInAnotherSession: false, ClaimDeadlinePassed: false);

    private static InspectionFacts NoResult(SettlementState recorded = SettlementState.ResultsPending, RefundKind recordedRefund = RefundKind.None)
        => Facts(ResultKind.NotShown, offered: null, recorded: recorded, recordedRefund: recordedRefund, resultsOffered: false);

    private static InspectionFacts Facts(
        ResultKind result,
        long? offered = Deposit,
        SettlementState recorded = SettlementState.ResultsPending,
        RefundKind recordedRefund = RefundKind.None,
        ActionKind? open = null,
        bool resultsOffered = true)
        => new(PlacardIdentityVerdict.Confirmed, resultsOffered, result, offered, recorded, recordedRefund, open, OwnsPlot: false, RefundWindowPassed: false, Absence(validated: false));
}
