using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Settlement.Model;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests.Settlement;

public sealed class SettlementGateTests
{
    private const ulong ContentId = 4_000_000_020;

    [Fact]
    public void RefundAndClaimShipUnvalidated()
    {
        Assert.False(SettlementCapabilities.RefundConfirmValidated);
        Assert.False(SettlementCapabilities.ClaimConfirmValidated);
        Assert.False(SettlementGate.Validated(ActionKind.Refund));
        Assert.False(SettlementGate.Validated(ActionKind.Claim));
    }

    [Theory]
    [InlineData((byte)ActionKind.Refund)]
    [InlineData((byte)ActionKind.Claim)]
    public void WithoutValidationOrAGrantYesIsUnreachable(byte raw)
    {
        var kind = (ActionKind)raw;
        Assert.False(SettlementGate.MayPress(kind, SettlementGate.Validated(kind), dryRun: false, null, ContentId, Plot(12, 5)));
    }

    [Fact]
    public void AGrantOpensExactlyItsOwnKindCharacterAndPlot()
    {
        var plot = Plot(12, 5);
        var refund = new SettlementGrant(ActionKind.Refund, ContentId, "Test Character", plot, ObservedAt);

        Assert.True(SettlementGate.MayPress(ActionKind.Refund, false, false, refund, ContentId, plot));
        Assert.False(SettlementGate.MayPress(ActionKind.Claim, false, false, refund, ContentId, plot));
        Assert.False(SettlementGate.MayPress(ActionKind.Refund, false, false, refund, ContentId + 1, plot));
        Assert.False(SettlementGate.MayPress(ActionKind.Refund, false, false, refund, ContentId, Plot(12, 6)));
        Assert.False(SettlementGate.MayPress(ActionKind.Refund, false, false, refund with { Used = true }, ContentId, plot));
        Assert.False(SettlementGate.MayPress(ActionKind.Entry, false, false, refund with { Kind = ActionKind.Entry }, ContentId, plot));
    }

    [Fact]
    public void ADryRunNeverPressesWhateverIsAllowed()
    {
        var plot = Plot(12, 5);
        var claim = new SettlementGrant(ActionKind.Claim, ContentId, "Test Character", plot, ObservedAt);

        Assert.False(SettlementGate.MayPress(ActionKind.Claim, true, dryRun: true, claim, ContentId, plot));
        Assert.True(SettlementGate.MayPress(ActionKind.Claim, true, dryRun: false, null, ContentId, plot));
    }

    [Fact]
    public void YesIsPressedOnlyOnTheRowsOfTheModesOwnResult()
    {
        Assert.Equal(Core.Game.Model.HousingText.ResultLost, ResultKinds.AcceptRow(ResultKind.Lost, SettlementMode.Refund));
        Assert.Equal(Core.Game.Model.HousingText.ResultForfeited, ResultKinds.AcceptRow(ResultKind.Forfeited, SettlementMode.Refund));
        Assert.Equal(Core.Game.Model.HousingText.ResultWon, ResultKinds.AcceptRow(ResultKind.Won, SettlementMode.Claim));
        Assert.Equal(Core.Game.Model.HousingText.ResultSoleEntrantWon, ResultKinds.AcceptRow(ResultKind.SoleEntrantWon, SettlementMode.Claim));
        Assert.Null(ResultKinds.AcceptRow(ResultKind.Won, SettlementMode.Refund));
        Assert.Null(ResultKinds.AcceptRow(ResultKind.Lost, SettlementMode.Claim));
        Assert.Null(ResultKinds.AcceptRow(ResultKind.Expired, SettlementMode.Refund));
        Assert.Null(ResultKinds.AcceptRow(ResultKind.Lost, SettlementMode.Inspect));
        Assert.Null(ResultKinds.AcceptRow(ResultKind.Unrecognized, SettlementMode.Refund));
    }

    [Fact]
    public void APressMayBeCutShortOnlyBeforeItsIntent()
    {
        Assert.True(SettlementSteps.MayInterrupt(SettlementStep.ViewResult));
        Assert.True(SettlementSteps.MayInterrupt(SettlementStep.Classify));
        Assert.False(SettlementSteps.MayInterrupt(SettlementStep.PersistIntent));
        Assert.False(SettlementSteps.MayInterrupt(SettlementStep.Press));
        Assert.False(SettlementSteps.MayInterrupt(SettlementStep.AwaitSettlement));
        Assert.True(SettlementSteps.MayInterrupt(SettlementStep.Finished));
    }
}
