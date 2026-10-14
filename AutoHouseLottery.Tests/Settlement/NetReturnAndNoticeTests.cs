using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Settlement.Model;
using static AutoHouseLottery.Tests.HousingFixtures;
using static AutoHouseLottery.Tests.Integration.IntegrationFixtures;

namespace AutoHouseLottery.Tests.Settlement;

public sealed class NetReturnAndNoticeTests
{
    private const long Refund = 3_000_000;
    private const long Travel = 1_000;

    [Fact]
    public void NetReturnIsTheRefundLessTheTravel()
    {
        var receipt = Receipt(Plot(12, 5), FirstCycle);
        var journal = With(Journal([], (receipt, SettlementState.Refunded)), receipt.ReceiptId, settlement => settlement with { ReceivedRefund = Refund, TravelSpent = Travel });

        var summary = NetReturn.Of(journal);

        Assert.Equal(new NetReturnSummary(Refund, Travel, 2_999_000, 0, 0), summary);
    }

    [Fact]
    public void DepositsNotYetReturnedArePendingAndWinsCountInNoSum()
    {
        var entered = Receipt(Plot(12, 5), FirstCycle);
        var forfeit = Receipt(Plot(12, 6), FirstCycle);
        var won = Receipt(Plot(12, 7), FirstCycle);
        var claimed = Receipt(Plot(12, 8), FirstCycle);
        var journal = Journal([], (entered, SettlementState.Entered), (forfeit, SettlementState.ForfeitRefundDue), (won, SettlementState.WonUnclaimed), (claimed, SettlementState.Claimed));
        journal = With(journal, forfeit.ReceiptId, settlement => settlement with { ExpectedRefund = Deposit / 2, Refund = RefundKind.Forfeit });
        journal = With(journal, claimed.ReceiptId, settlement => settlement with { TravelSpent = Travel });

        var summary = NetReturn.Of(journal);

        Assert.Equal(Deposit + Deposit / 2, summary.StillPending);
        Assert.Equal(0, summary.RefundReceived);
        Assert.Equal(Travel, summary.TravelSpent);
        Assert.Equal(-Travel, summary.NetReturned);
    }

    [Fact]
    public void AmountsNeverSeenAreCountedApartNotGuessed()
    {
        var outside = Receipt(Plot(12, 5), FirstCycle);
        var imported = Receipt(null, FirstCycle, ReceiptEvidence.ImportedFromGame);
        var journal = Journal([], (outside, SettlementState.Refunded), (imported, SettlementState.Entered));

        var summary = NetReturn.Of(journal);

        Assert.Equal(new NetReturnSummary(0, 0, 0, 0, 2), summary);
    }

    [Fact]
    public void AClaimInQuestionIsNotAPendingDeposit()
    {
        var claim = Receipt(Plot(12, 5), FirstCycle);
        var refund = Receipt(Plot(12, 6), FirstCycle);
        var journal = Journal([], (claim, SettlementState.NeedsReconciliation), (refund, SettlementState.NeedsReconciliation));
        journal = With(journal, refund.ReceiptId, settlement => settlement with { Refund = RefundKind.Loss, ExpectedRefund = Deposit });

        Assert.Equal(Deposit, NetReturn.Of(journal).StillPending);
    }

    [Fact]
    public void ResultsAvailableIsAnnouncedOncePerEntry()
    {
        var entered = Receipt(Plot(12, 5), FirstCycle);
        var journal = Journal([], (entered, SettlementState.Entered));
        var results = ResultsPhase(FirstCycle);
        var now = results.PhaseStartUtc.AddHours(1);

        Assert.Empty(SettlementNotices.Due(journal, VerifiedEntryPhase(), LotteryCalendar.Global, now, _ => false));
        var notice = Assert.Single(SettlementNotices.Due(journal, results, LotteryCalendar.Global, now, _ => false));
        Assert.Equal(SettlementNoticeKind.ResultsAvailable, notice.Kind);
        Assert.Empty(SettlementNotices.Due(journal, results, LotteryCalendar.Global, now, key => key == notice.Key));
    }

    [Fact]
    public void AWinFoundInTheBackgroundIsAnnouncedOnce()
    {
        var won = Receipt(Plot(12, 5), FirstCycle);
        var journal = Journal([], (won, SettlementState.WonUnclaimed));
        var results = ResultsPhase(FirstCycle);
        var now = results.PhaseStartUtc.AddHours(1);

        var notice = Assert.Single(SettlementNotices.Due(journal, results, LotteryCalendar.Global, now, _ => false));
        Assert.Equal(SettlementNoticeKind.Won, notice.Kind);
        Assert.Equal(results.PhaseEndUtc, notice.DeadlineUtc);
        Assert.Empty(SettlementNotices.Due(journal, results, LotteryCalendar.Global, now, key => key == notice.Key));
    }

    [Fact]
    public void AWinFoundAfterItsClaimDeadlineIsNotAnnounced()
    {
        var won = Receipt(Plot(12, 5), FirstCycle);
        var journal = Journal([], (won, SettlementState.WonUnclaimed));
        var results = ResultsPhase(FirstCycle);

        Assert.Empty(SettlementNotices.Due(journal, results, LotteryCalendar.Global, results.PhaseEndUtc, _ => false));
        Assert.Empty(SettlementNotices.Due(journal, results, LotteryCalendar.Global, results.PhaseEndUtc.AddDays(3), _ => false));
    }

    [Fact]
    public void AClaimDeadlineIsAnnouncedWithinItsLastDay()
    {
        var won = Receipt(Plot(12, 5), FirstCycle);
        var journal = Journal([], (won, SettlementState.WonUnclaimed));
        var results = ResultsPhase(FirstCycle);
        static bool WinAnnounced(string key) => key.StartsWith(nameof(SettlementNoticeKind.Won) + ":", StringComparison.Ordinal);

        Assert.Empty(SettlementNotices.Due(journal, results, LotteryCalendar.Global, results.PhaseEndUtc.AddHours(-SettlementNotices.ClaimLeadHours - 1), WinAnnounced));
        var notice = Assert.Single(SettlementNotices.Due(journal, results, LotteryCalendar.Global, results.PhaseEndUtc.AddHours(-2), WinAnnounced));
        Assert.Equal(SettlementNoticeKind.ClaimDeadline, notice.Kind);
        Assert.Equal(results.PhaseEndUtc, notice.DeadlineUtc);
        Assert.Empty(SettlementNotices.Due(journal, results, LotteryCalendar.Global, results.PhaseEndUtc.AddMinutes(1), WinAnnounced));
    }

    [Fact]
    public void TheDeadlineTheDialogNamedWinsOverTheEstimate()
    {
        var lost = Receipt(Plot(12, 5), FirstCycle);
        var later = ResultsPhase(new CycleKey(GameRegion.Global, 10));
        var shown = CycleStart.AddDays(40);
        var journal = With(Journal([], (lost, SettlementState.LostRefundDue)), lost.ReceiptId, settlement => settlement with { RefundDeadlineUtc = shown });

        var notice = Assert.Single(SettlementNotices.Due(journal, later, LotteryCalendar.Global, shown.AddDays(-1), _ => false));
        Assert.Equal(shown, notice.DeadlineUtc);
        Assert.Empty(SettlementNotices.Due(journal, later, LotteryCalendar.Global, shown.AddDays(1), _ => false));
    }

    [Fact]
    public void ARefundDeadlineComesNinetyDaysAfterTheResultsOpen()
    {
        var lost = Receipt(Plot(12, 5), FirstCycle);
        var journal = Journal([], (lost, SettlementState.LostRefundDue));
        var later = ResultsPhase(new CycleKey(GameRegion.Global, 10));
        var resultsStart = CycleStart + LotteryCalendar.EntryPeriod;
        var deadline = resultsStart.AddDays(SettlementDeadlines.RefundWindowDays);

        Assert.Empty(SettlementNotices.Due(journal, later, LotteryCalendar.Global, deadline.AddDays(-SettlementNotices.RefundLeadDays - 1), _ => false));
        var notice = Assert.Single(SettlementNotices.Due(journal, later, LotteryCalendar.Global, deadline.AddDays(-1), _ => false));
        Assert.Equal(SettlementNoticeKind.RefundDeadline, notice.Kind);
        Assert.Equal(deadline, notice.DeadlineUtc);
    }

    [Fact]
    public void ADeadlinePassedWhileClosedLeavesTheReceiptInPlace()
    {
        var lost = Receipt(Plot(12, 5), FirstCycle);
        var journal = Journal([], (lost, SettlementState.LostRefundDue));
        var later = ResultsPhase(new CycleKey(GameRegion.Global, 20));
        var afterDeadline = CycleStart + LotteryCalendar.EntryPeriod + TimeSpan.FromDays(SettlementDeadlines.RefundWindowDays + 1);

        Assert.Empty(SettlementNotices.Due(journal, later, LotteryCalendar.Global, afterDeadline, _ => false));
        Assert.Single(journal.Receipts);
    }

    [Fact]
    public void ADeadlineADialogShowedIsTrustedOnlyNearTheEstimate()
    {
        var estimate = CycleStart.AddDays(SettlementDeadlines.RefundWindowDays);

        Assert.True(SettlementDeadlines.PlausibleShownDeadline(estimate, estimate));
        Assert.True(SettlementDeadlines.PlausibleShownDeadline(estimate.AddDays(-SettlementDeadlines.ShownDeadlineToleranceDays), estimate));
        Assert.True(SettlementDeadlines.PlausibleShownDeadline(estimate.AddDays(SettlementDeadlines.ShownDeadlineToleranceDays), estimate));
        Assert.False(SettlementDeadlines.PlausibleShownDeadline(estimate.AddDays(SettlementDeadlines.ShownDeadlineToleranceDays).AddMinutes(1), estimate));
        Assert.False(SettlementDeadlines.PlausibleShownDeadline(estimate.AddDays(-40), estimate));
        Assert.False(SettlementDeadlines.PlausibleShownDeadline(estimate, null));
    }

    private static JournalSnapshot With(JournalSnapshot journal, Guid receiptId, Func<EntrySettlement, EntrySettlement> change)
    {
        var settlements = journal.Settlements.ToBuilder();
        for (var index = 0; index < settlements.Count; index++)
        {
            if (settlements[index].ReceiptId == receiptId)
            {
                settlements[index] = change(settlements[index]);
            }
        }

        return journal with { Settlements = settlements.ToImmutable() };
    }
}
