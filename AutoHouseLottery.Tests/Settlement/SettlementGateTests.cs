using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Settlement.Model;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests.Settlement;

public sealed class SettlementGateTests
{
    private const ulong ContentId = 4_000_000_020;

    private static readonly Guid receiptId = Guid.NewGuid();

    [Fact]
    public void RefundClaimAndAbsenceShipUnvalidated()
    {
        Assert.False(SettlementGate.Validated(ActionKind.Refund));
        Assert.False(SettlementGate.Validated(ActionKind.Claim));
        Assert.False(SettlementGate.Validated(ActionKind.Entry));
        Assert.True(SettlementGate.PlayerDryRun(SettlementMode.Refund));
        Assert.True(SettlementGate.PlayerDryRun(SettlementMode.Claim));
    }

    [Theory]
    [InlineData((byte)ActionKind.Refund)]
    [InlineData((byte)ActionKind.Claim)]
    public void WithoutValidationOrAGrantYesIsUnreachable(byte raw)
    {
        var kind = (ActionKind)raw;
        Assert.False(SettlementGate.MayPress(kind, SettlementGate.Validated(kind), dryRun: false, null, ContentId, Plot(12, 5), receiptId));
    }

    [Fact]
    public void AGrantOpensExactlyItsOwnKindCharacterReceiptAndPlot()
    {
        var plot = Plot(12, 5);
        var refund = new SettlementGrant(ActionKind.Refund, ContentId, "Test Character", plot, receiptId, ObservedAt);

        Assert.True(SettlementGate.MayPress(ActionKind.Refund, false, false, refund, ContentId, plot, receiptId));
        Assert.False(SettlementGate.MayPress(ActionKind.Claim, false, false, refund, ContentId, plot, receiptId));
        Assert.False(SettlementGate.MayPress(ActionKind.Refund, false, false, refund, ContentId + 1, plot, receiptId));
        Assert.False(SettlementGate.MayPress(ActionKind.Refund, false, false, refund, ContentId, Plot(12, 6), receiptId));
        Assert.False(SettlementGate.MayPress(ActionKind.Refund, false, false, refund, ContentId, plot, Guid.NewGuid()));
        Assert.False(SettlementGate.MayPress(ActionKind.Refund, false, false, refund with { Used = true }, ContentId, plot, receiptId));
        Assert.False(SettlementGate.MayPress(ActionKind.Entry, false, false, refund with { Kind = ActionKind.Entry }, ContentId, plot, receiptId));
    }

    [Fact]
    public void ARequestedDryRunAlwaysWins()
    {
        var plot = Plot(12, 5);
        var claim = new SettlementGrant(ActionKind.Claim, ContentId, "Test Character", plot, receiptId, ObservedAt);

        Assert.False(SettlementGate.MayPress(ActionKind.Claim, true, dryRun: true, claim, ContentId, plot, receiptId));
        Assert.False(SettlementGate.MayPress(ActionKind.Claim, false, dryRun: true, claim, ContentId, plot, receiptId));
        Assert.True(SettlementGate.MayPress(ActionKind.Claim, true, dryRun: false, null, ContentId, plot, receiptId));
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
