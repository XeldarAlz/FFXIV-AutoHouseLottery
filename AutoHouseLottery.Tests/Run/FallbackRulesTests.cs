using AutoHouseLottery.Core.Selection.Model;
using AutoHouseLottery.Core.Tasks.Model;

namespace AutoHouseLottery.Tests.Run;

public sealed class FallbackRulesTests
{
    private const int Limit = RunPolicy.MaximumInspections;

    [Theory]
    [InlineData((byte)EntryFailure.Sold)]
    [InlineData((byte)EntryFailure.WrongPhase)]
    [InlineData((byte)EntryFailure.NotLottery)]
    [InlineData((byte)EntryFailure.ChangedPrice)]
    [InlineData((byte)EntryFailure.OverBudget)]
    [InlineData((byte)EntryFailure.Ineligible)]
    public void BestMatchMovesOnAfterAConclusiveFailureBeforePayment(byte raw)
    {
        var failure = (EntryFailure)raw;
        Assert.Equal(FallbackDecision.TryNextPlot, FallbackRules.Decide(RunMode.BestMatch, failure, submissionStarted: false, 1, Limit, hasNextTarget: true));
    }

    [Theory]
    [InlineData((byte)EntryFailure.Sold)]
    [InlineData((byte)EntryFailure.ChangedPrice)]
    public void ExactPlotNeverMoves(byte raw)
    {
        var failure = (EntryFailure)raw;
        Assert.Equal(FallbackDecision.FinishCharacter, FallbackRules.Decide(RunMode.Exact, failure, submissionStarted: false, 1, Limit, hasNextTarget: true));
    }

    [Theory]
    [InlineData((byte)EntryFailure.TravelFailed)]
    [InlineData((byte)EntryFailure.IdentityUnverified)]
    [InlineData((byte)EntryFailure.WrongPlacard)]
    [InlineData((byte)EntryFailure.SaleUnreadable)]
    [InlineData((byte)EntryFailure.DepositUnreadable)]
    [InlineData((byte)EntryFailure.ConfirmationNotShown)]
    [InlineData((byte)EntryFailure.GilUnknown)]
    public void UnclearFailureAsksThePlayerInsteadOfMovingOn(byte raw)
    {
        var failure = (EntryFailure)raw;
        Assert.Equal(FallbackDecision.AskPlayer, FallbackRules.Decide(RunMode.BestMatch, failure, submissionStarted: false, 1, Limit, hasNextTarget: true));
    }

    [Theory]
    [InlineData((byte)EntryFailure.Rejected)]
    [InlineData((byte)EntryFailure.AlreadyEntered)]
    [InlineData((byte)EntryFailure.CollectRefundFirst)]
    [InlineData((byte)EntryFailure.CycleChanged)]
    public void FailureAboutTheCharacterEndsItsTurnWithoutAnotherPlot(byte raw)
    {
        var failure = (EntryFailure)raw;
        Assert.Equal(FallbackDecision.FinishCharacter, FallbackRules.Decide(RunMode.BestMatch, failure, submissionStarted: false, 1, Limit, hasNextTarget: true));
    }

    [Fact]
    public void NothingMovesOnceAPressMayHaveReachedTheGame()
    {
        Assert.Equal(FallbackDecision.AskPlayer, FallbackRules.Decide(RunMode.BestMatch, EntryFailure.Sold, submissionStarted: true, 1, Limit, hasNextTarget: true));
    }

    [Fact]
    public void FifthInspectionIsTheLast()
    {
        Assert.Equal(FallbackDecision.TryNextPlot, FallbackRules.Decide(RunMode.BestMatch, EntryFailure.Sold, false, Limit - 1, Limit, true));
        Assert.Equal(FallbackDecision.FinishCharacter, FallbackRules.Decide(RunMode.BestMatch, EntryFailure.Sold, false, Limit, Limit, true));
        Assert.Equal(FallbackDecision.FinishCharacter, FallbackRules.Decide(RunMode.BestMatch, EntryFailure.Sold, false, 1, Limit, false));
    }
}
