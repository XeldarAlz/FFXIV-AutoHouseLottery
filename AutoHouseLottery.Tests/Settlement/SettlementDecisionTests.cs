using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Settlement.Model;
using AutoHouseLottery.Core.Tasks.Model;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests.Settlement;

public sealed class SettlementDecisionTests
{
    [Theory]
    [InlineData((byte)ActionKind.Refund, (byte)InspectionOutcome.Refunded, (byte)AttemptResolution.Confirm)]
    [InlineData((byte)ActionKind.Refund, (byte)InspectionOutcome.LostRefundDue, (byte)AttemptResolution.Release)]
    [InlineData((byte)ActionKind.Refund, (byte)InspectionOutcome.ForfeitRefundDue, (byte)AttemptResolution.Release)]
    [InlineData((byte)ActionKind.Refund, (byte)InspectionOutcome.Claimed, (byte)AttemptResolution.Keep)]
    [InlineData((byte)ActionKind.Refund, (byte)InspectionOutcome.OutcomeUnknown, (byte)AttemptResolution.Keep)]
    [InlineData((byte)ActionKind.Claim, (byte)InspectionOutcome.Claimed, (byte)AttemptResolution.Confirm)]
    [InlineData((byte)ActionKind.Claim, (byte)InspectionOutcome.WonUnclaimed, (byte)AttemptResolution.Release)]
    [InlineData((byte)ActionKind.Claim, (byte)InspectionOutcome.Refunded, (byte)AttemptResolution.Keep)]
    [InlineData((byte)ActionKind.Claim, (byte)InspectionOutcome.LostRefundDue, (byte)AttemptResolution.Keep)]
    public void AnOwnPressInQuestionIsSettledOnlyByItsOwnKindOfResult(byte kind, byte outcome, byte expected)
    {
        Assert.Equal((AttemptResolution)expected, SettlementDecisions.ResolveOpenAttempt((ActionKind)kind, (InspectionOutcome)outcome));
    }

    [Fact]
    public void OnlyAnIntentNeverRecordedAsDispatchedIsSetAsideBeforeItIsConfirmed()
    {
        Assert.True(SettlementDecisions.NeedsQuestionBeforeConfirm(AttemptState.Prepared));
        Assert.False(SettlementDecisions.NeedsQuestionBeforeConfirm(AttemptState.Dispatched));
        Assert.False(SettlementDecisions.NeedsQuestionBeforeConfirm(AttemptState.NeedsReconciliation));
        Assert.True(AttemptTransitions.IsLegal(AttemptState.Dispatched, AttemptState.Confirmed));
    }

    [Fact]
    public void ALoadedPluginThatAnswersDialogsIsFoundByItsInternalName()
    {
        Assert.Equal("YesAlready", AutoAnswerPlugins.FirstLoaded([new InstalledPlugin("Lifestream", "Lifestream", true), new InstalledPlugin("YesAlready", "YesAlready", true)]));
        Assert.Null(AutoAnswerPlugins.FirstLoaded([new InstalledPlugin("YesAlready", "YesAlready", false)]));
        Assert.Equal("YesAlready", AutoAnswerPlugins.FirstLoaded([new InstalledPlugin("YesAlready", string.Empty, true)]));
        Assert.Null(AutoAnswerPlugins.FirstLoaded([new InstalledPlugin("Lifestream", "Yes Already", true)]));
    }

    [Theory]
    [InlineData("yesalready")]
    [InlineData("YESALREADY")]
    [InlineData("YesAlreadyFork")]
    [InlineData("MyYesAlready")]
    public void AnyLoadedPluginNamedLikeYesAlreadyCounts(string internalName)
    {
        Assert.Equal("Look-alike", AutoAnswerPlugins.FirstLoaded([new InstalledPlugin(internalName, "Look-alike", true)]));
        Assert.Null(AutoAnswerPlugins.FirstLoaded([new InstalledPlugin(internalName, "Look-alike", false)]));
    }

    [Fact]
    public void OnlyYesAlreadyItselfIsPausedAndOnlyWhileItsStopRequestsCanBeReached()
    {
        var yesAlready = new InstalledPlugin("YesAlready", "YesAlready", true);
        var fork = new InstalledPlugin("YesAlreadyFork", "Look-alike", true);

        Assert.Null(AutoAnswerPlugins.FirstUnpausable([yesAlready], stopRequestsReachable: true));
        Assert.Equal("YesAlready", AutoAnswerPlugins.FirstUnpausable([yesAlready], stopRequestsReachable: false));
        Assert.Equal("Look-alike", AutoAnswerPlugins.FirstUnpausable([yesAlready, fork], stopRequestsReachable: true));
        Assert.Equal("Look-alike", AutoAnswerPlugins.FirstUnpausable([new InstalledPlugin("yesalready", "Look-alike", true)], stopRequestsReachable: true));
        Assert.Null(AutoAnswerPlugins.FirstUnpausable([fork with { Loaded = false }], stopRequestsReachable: true));
    }

    [Fact]
    public void ARefundIsCollectedOnlyForALossOrForfeitThatIsRecordedAsOwed()
    {
        var owedLoss = new InspectionVerdict(InspectionOutcome.LostRefundDue, Deposit, InspectionReason.None);
        var owedForfeit = new InspectionVerdict(InspectionOutcome.ForfeitRefundDue, Deposit / 2, InspectionReason.None);

        Assert.Equal(VisitAction.CollectRefund, SettlementDecisions.Act(SettlementMode.Refund, ResultKind.Lost, owedLoss, attemptOpenBeforeVisit: false));
        Assert.Equal(VisitAction.CollectRefund, SettlementDecisions.Act(SettlementMode.Refund, ResultKind.Forfeited, owedForfeit, false));
        Assert.Equal(VisitAction.Close, SettlementDecisions.Act(SettlementMode.Refund, ResultKind.Lost, owedLoss with { Refund = null }, false));
        Assert.Equal(VisitAction.Close, SettlementDecisions.Act(SettlementMode.Refund, ResultKind.Lost, owedForfeit, false));
        Assert.Equal(VisitAction.Close, SettlementDecisions.Act(SettlementMode.Refund, ResultKind.Lost, owedLoss, attemptOpenBeforeVisit: true));
        Assert.Equal(VisitAction.Close, SettlementDecisions.Act(SettlementMode.Inspect, ResultKind.Lost, owedLoss, false));
    }

    [Fact]
    public void AWinIsNeverCollectedAsARefundAndALossIsNeverClaimed()
    {
        var won = new InspectionVerdict(InspectionOutcome.WonUnclaimed, null, InspectionReason.None);
        var owedLoss = new InspectionVerdict(InspectionOutcome.LostRefundDue, Deposit, InspectionReason.None);

        Assert.Equal(VisitAction.Claim, SettlementDecisions.Act(SettlementMode.Claim, ResultKind.Won, won, false));
        Assert.Equal(VisitAction.Claim, SettlementDecisions.Act(SettlementMode.Claim, ResultKind.SoleEntrantWon, won, false));
        Assert.Equal(VisitAction.Close, SettlementDecisions.Act(SettlementMode.Refund, ResultKind.Won, won, false));
        Assert.Equal(VisitAction.Close, SettlementDecisions.Act(SettlementMode.Claim, ResultKind.Lost, owedLoss, false));
        Assert.Equal(VisitAction.Close, SettlementDecisions.Act(SettlementMode.Inspect, ResultKind.Won, won, false));
    }

    [Fact]
    public void ARowThatDisagreesWithTheVerdictClosesTheDialog()
    {
        var claimed = new InspectionVerdict(InspectionOutcome.Claimed, null, InspectionReason.None);
        var unknown = InspectionVerdict.Unknown(InspectionReason.ContradictsRecord);

        Assert.Equal(VisitAction.Close, SettlementDecisions.Act(SettlementMode.Refund, ResultKind.Lost, claimed, false));
        Assert.Equal(VisitAction.Close, SettlementDecisions.Act(SettlementMode.Claim, ResultKind.Won, unknown, false));
    }
}
