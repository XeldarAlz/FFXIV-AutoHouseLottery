using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Settlement.Model;
using AutoHouseLottery.Core.Tasks.Model;

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
        Assert.Null(AutoAnswerPlugins.FirstLoaded([new InstalledPlugin("yesalready", "Look-alike", true)]));
        Assert.Equal("YesAlready", AutoAnswerPlugins.FirstLoaded([new InstalledPlugin("YesAlready", string.Empty, true)]));
    }
}
