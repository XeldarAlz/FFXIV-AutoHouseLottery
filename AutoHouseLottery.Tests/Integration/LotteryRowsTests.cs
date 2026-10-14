using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Integration.Model;
using AutoHouseLottery.Core.Persistence.Model;
using static AutoHouseLottery.Tests.HousingFixtures;
using static AutoHouseLottery.Tests.Integration.IntegrationFixtures;

namespace AutoHouseLottery.Tests.Integration;

public sealed class LotteryRowsTests
{
    private static readonly LotterySchedule schedule = new(GameRegion.Global, CycleStart, FirstCycle.Number, 1);

    [Fact]
    public void EntryOfTheOpenCycleIsActiveUntilTheEntryPeriodEnds()
    {
        var row = Single(Journal([], (Receipt(Plot(12, 5), FirstCycle), SettlementState.Entered)), VerifiedEntryPhase());

        Assert.Equal(LotterySection.Active, row.Section);
        Assert.Equal(LotteryAction.ViewPlot, row.Action);
        Assert.Equal(CycleStart + LotteryCalendar.EntryPeriod, row.DeadlineUtc);
        Assert.Equal(Deposit, row.Deposit);
    }

    [Fact]
    public void EntryInItsResultsPeriodNeedsItsResultChecked()
    {
        var row = Single(Journal([], (Receipt(Plot(12, 5), FirstCycle), SettlementState.Entered)), ResultsPhase(FirstCycle));

        Assert.Equal(LotterySection.ActionNeeded, row.Section);
        Assert.Equal(LotteryAction.CheckResult, row.Action);
        Assert.Equal(CycleStart + LotteryCalendar.CycleLength, row.DeadlineUtc);
    }

    [Fact]
    public void EarlierCycleEntryUsesTheCalendarForItsDeadline()
    {
        var current = VerifiedEntryPhase() with { Cycle = SecondCycle };
        var row = Single(Journal([], (Receipt(Plot(12, 5), FirstCycle), SettlementState.Entered)), current);

        Assert.Equal(LotteryAction.CheckResult, row.Action);
        Assert.Equal(CycleStart + LotteryCalendar.CycleLength, row.DeadlineUtc);
    }

    [Fact]
    public void WinAndRefundEachGetTheirOwnAction()
    {
        Assert.Equal(LotteryAction.ClaimHouse, Single(Journal([], (Receipt(Plot(1, 1), FirstCycle), SettlementState.WonUnclaimed)), ResultsPhase(FirstCycle)).Action);
        Assert.Equal(LotteryAction.CollectRefund, Single(Journal([], (Receipt(Plot(1, 1), FirstCycle), SettlementState.LostRefundDue)), ResultsPhase(FirstCycle)).Action);
        Assert.Equal(LotteryAction.VerifyOutcome, Single(Journal([], (Receipt(Plot(1, 1), FirstCycle), SettlementState.RefundPending)), ResultsPhase(FirstCycle)).Action);
    }

    [Fact]
    public void SettledEntriesAreHistory()
    {
        var row = Single(Journal([], (Receipt(Plot(1, 1), FirstCycle), SettlementState.Refunded)), ResultsPhase(SecondCycle));

        Assert.Equal(LotterySection.History, row.Section);
        Assert.Equal(LotteryAction.ViewReceipt, row.Action);
        Assert.Null(row.DeadlineUtc);
    }

    [Fact]
    public void UncertainAttemptNeedsItsOutcomeVerifiedAndRejectedOnesAreHistory()
    {
        var rows = LotteryRows.Build(Journal([Attempt(Plot(12, 5), AttemptState.NeedsReconciliation), Attempt(Plot(3, 1), AttemptState.Rejected, SecondCycle)]), VerifiedEntryPhase(), schedule);

        Assert.Equal(2, rows.Length);
        Assert.Equal(LotteryRowState.Uncertain, rows[0].State);
        Assert.Equal(LotteryAction.VerifyOutcome, rows[0].Action);
        Assert.Equal(LotteryRowState.Rejected, rows[1].State);
        Assert.Equal(LotterySection.History, rows[1].Section);
    }

    [Fact]
    public void ConfirmedAttemptShowsOnceThroughItsReceipt()
    {
        var receipt = Receipt(Plot(12, 5), FirstCycle);
        var rows = LotteryRows.Build(Journal([Attempt(Plot(12, 5), AttemptState.Confirmed)], (receipt, SettlementState.Entered)), VerifiedEntryPhase(), schedule);

        Assert.Equal(receipt.ReceiptId, Assert.Single(rows).Id);
    }

    [Fact]
    public void ImportedEntryKeepsItsUnknownFieldsUnknown()
    {
        var imported = Receipt(null, FirstCycle, ReceiptEvidence.ImportedFromGame) with { EnteredUtc = null, AddressText = "Plot 5, 12 Ward, Mist" };
        var row = Single(Journal([], (imported, SettlementState.Entered)), VerifiedEntryPhase());

        Assert.True(row.Imported);
        Assert.Null(row.Plot);
        Assert.Null(row.Deposit);
        Assert.Null(row.EnteredUtc);
        Assert.Equal("Plot 5, 12 Ward, Mist", row.AddressText);
    }

    private static LotteryRow Single(JournalSnapshot snapshot, CyclePosition current) => Assert.Single(LotteryRows.Build(snapshot, current, schedule));
}
