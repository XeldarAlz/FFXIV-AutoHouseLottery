using AutoHouseLottery.Core.Tasks.Model;

namespace AutoHouseLottery.Tests.Run;

public sealed class RunStateMachineTests
{
    public static TheoryData<byte> PreSubmissionSteps()
    {
        var data = new TheoryData<byte>();
        for (var step = EntryStep.Preflight; step <= EntryStep.PrepareEntry; step++)
        {
            data.Add((byte)step);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PreSubmissionSteps))]
    public void AnyStepBeforeTheIntentCanPauseStopOrAskThePlayer(byte raw)
    {
        var step = (EntryStep)raw;
        Assert.True(RunTransitions.IsLegal(step, EntryStep.Paused));
        Assert.True(RunTransitions.IsLegal(step, EntryStep.Stopped));
        Assert.True(RunTransitions.IsLegal(step, EntryStep.NeedsPlayer));
        Assert.False(RunTransitions.IsLegal(step, EntryStep.NeedsReconciliation) && step != EntryStep.Preflight);
    }

    [Theory]
    [InlineData((byte)EntryStep.SubmitOnce)]
    [InlineData((byte)EntryStep.AwaitReceipt)]
    [InlineData((byte)EntryStep.PersistReceipt)]
    public void OncePressedTheRunCannotPauseOrStopOnlyResolve(byte raw)
    {
        var step = (EntryStep)raw;
        Assert.False(RunTransitions.IsLegal(step, EntryStep.Paused));
        Assert.False(RunTransitions.IsLegal(step, EntryStep.Stopped));
        Assert.True(RunTransitions.IsLegal(step, EntryStep.NeedsReconciliation));
        Assert.Equal(EntryStep.NeedsReconciliation, RunTransitions.OnInterrupt(step));
    }

    [Fact]
    public void NoStepAfterThePressLeadsBackToAnotherPress()
    {
        for (var from = EntryStep.SubmitOnce; from <= EntryStep.FinishCharacter; from++)
        {
            for (var to = EntryStep.Ready; to <= EntryStep.PersistIntent; to++)
            {
                Assert.False(RunTransitions.IsLegal(from, to), $"{from} -> {to}");
            }

            Assert.False(RunTransitions.IsLegal(from, EntryStep.SubmitOnce), $"{from} -> SubmitOnce");
        }
    }

    [Fact]
    public void IntentAndPressAreNotSplitByAPause()
    {
        Assert.False(RunTransitions.IsLegal(EntryStep.PersistIntent, EntryStep.Paused));
        Assert.False(EntrySteps.MayPauseAt(EntryStep.PersistIntent));
        Assert.True(RunTransitions.IsLegal(EntryStep.PersistIntent, EntryStep.SubmitOnce));
    }

    [Fact]
    public void FallbackGoesBackToTravelOnlyBeforeTheIntent()
    {
        Assert.True(RunTransitions.IsLegal(EntryStep.ReadLivePlot, EntryStep.Travel));
        Assert.True(RunTransitions.IsLegal(EntryStep.VerifyEligibility, EntryStep.Travel));
        Assert.True(RunTransitions.IsLegal(EntryStep.PrepareEntry, EntryStep.Travel));
        Assert.False(RunTransitions.IsLegal(EntryStep.PersistIntent, EntryStep.Travel));
        Assert.False(RunTransitions.IsLegal(EntryStep.AwaitReceipt, EntryStep.Travel));
    }

    [Fact]
    public void InterruptBeforeThePressStopsAndTerminalStepsStay()
    {
        Assert.Equal(EntryStep.Stopped, RunTransitions.OnInterrupt(EntryStep.Travel));
        Assert.Equal(EntryStep.Stopped, RunTransitions.OnInterrupt(EntryStep.PersistIntent));
        Assert.Equal(EntryStep.Completed, RunTransitions.OnInterrupt(EntryStep.Completed));
        Assert.Equal(EntryStep.NeedsReconciliation, RunTransitions.OnInterrupt(EntryStep.NeedsReconciliation));
    }

    [Fact]
    public void StagesFollowThePlainStepOrder()
    {
        Assert.Equal(RunStage.GettingReady, EntrySteps.StageOf(EntryStep.Preflight));
        Assert.Equal(RunStage.Travelling, EntrySteps.StageOf(EntryStep.VerifyArrival));
        Assert.Equal(RunStage.Approaching, EntrySteps.StageOf(EntryStep.OpenPlacard));
        Assert.Equal(RunStage.Checking, EntrySteps.StageOf(EntryStep.ReadLivePlot));
        Assert.Equal(RunStage.Entering, EntrySteps.StageOf(EntryStep.SubmitOnce));
        Assert.Equal(RunStage.Verifying, EntrySteps.StageOf(EntryStep.AwaitReceipt));
        Assert.Equal(5, EntrySteps.StageCount);
    }
}
