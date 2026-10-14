using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Integration.Model;
using AutoHouseLottery.Core.Persistence.Model;
using static AutoHouseLottery.Tests.HousingFixtures;
using static AutoHouseLottery.Tests.Integration.IntegrationFixtures;

namespace AutoHouseLottery.Tests.Integration;

public sealed class ResultChecksTests
{
    [Fact]
    public void EarlierCycleAttemptInQuestionBecomesAResultCheckNotABlocker()
    {
        var earlier = Attempt(Plot(12, 5), AttemptState.NeedsReconciliation, FirstCycle);
        var snapshot = Journal([earlier]);

        var check = Assert.Single(ResultChecks.Due(snapshot, SecondCycle));
        Assert.Equal(ResultCheckSource.UncertainAttempt, check.Source);
        Assert.Equal(earlier.AttemptId, check.Id);
        Assert.Equal(EntrySlotState.Unknown, ParticipationRules.FromJournal(snapshot, ContentId, HomeWorld, SecondCycle).State);
        Assert.Equal(OutstandingKind.None, OutstandingRules.Find(snapshot, ContentId, SecondCycle).Kind);
    }

    [Fact]
    public void ThisCyclesAttemptInQuestionStillHoldsItsSlot()
    {
        var snapshot = Journal([Attempt(Plot(12, 5), AttemptState.Dispatched, SecondCycle)]);

        Assert.Empty(ResultChecks.Due(snapshot, SecondCycle));
        Assert.Equal(EntrySlotState.Unresolved, ParticipationRules.FromJournal(snapshot, ContentId, HomeWorld, SecondCycle).State);
    }

    [Fact]
    public void OnlyUnsettledEarlierReceiptsNeedTheirResultRead()
    {
        var unsettled = Receipt(Plot(1, 1), FirstCycle);
        var snapshot = Journal(
            [Attempt(Plot(3, 3), AttemptState.Rejected, FirstCycle)],
            (unsettled, SettlementState.Entered),
            (Receipt(Plot(2, 2), FirstCycle), SettlementState.Refunded),
            (Receipt(Plot(4, 4), SecondCycle), SettlementState.Entered));

        var check = Assert.Single(ResultChecks.Due(snapshot, SecondCycle));
        Assert.Equal(unsettled.ReceiptId, check.Id);
        Assert.Equal(ResultCheckSource.UnsettledReceipt, check.Source);
        Assert.Equal(OutstandingKind.ResultUnchecked, OutstandingRules.Find(snapshot, ContentId, SecondCycle).Kind);
    }

    [Fact]
    public void EarlierCycleAttemptShowsAsAResultToCheck()
    {
        var current = VerifiedEntryPhase() with { Cycle = SecondCycle };

        var row = Assert.Single(LotteryRows.Build(Journal([Attempt(Plot(12, 5), AttemptState.NeedsReconciliation, FirstCycle)]), current, null));

        Assert.Equal(LotteryAction.CheckResult, row.Action);
        Assert.Equal(LotterySection.ActionNeeded, row.Section);
    }
}
