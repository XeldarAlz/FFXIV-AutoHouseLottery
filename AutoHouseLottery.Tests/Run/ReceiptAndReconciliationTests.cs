using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Tasks.Model;
using static AutoHouseLottery.Tests.HousingFixtures;
using static AutoHouseLottery.Tests.Integration.IntegrationFixtures;

namespace AutoHouseLottery.Tests.Run;

public sealed class ReceiptAndReconciliationTests
{
    private const long GilBefore = 10_000_000;

    [Fact]
    public void ReadingOnThePlotAndTheExactDepositConfirm()
    {
        var result = ReceiptCorrelation.Correlate(Facts(GilBefore - Deposit, RecordReading(12, 5), true));

        Assert.Equal(ReceiptVerdict.Confirmed, result.Verdict);
        Assert.Equal(LotteryNumber, result.LotteryNumber);
    }

    [Fact]
    public void GilAloneNeverConfirms()
    {
        Assert.Equal(ReceiptVerdict.Uncertain, ReceiptCorrelation.Correlate(Facts(GilBefore - Deposit, null, null)).Verdict);
        Assert.Equal(ReceiptVerdict.Uncertain, ReceiptCorrelation.Correlate(Facts(GilBefore - Deposit, Reading(LotterySnapshotState.Unloaded), null)).Verdict);
        Assert.Equal(ReceiptVerdict.Uncertain, ReceiptCorrelation.Correlate(Facts(GilBefore - Deposit, TextReading(null), null)).Verdict);
    }

    [Fact]
    public void ReadingWithoutTheMatchingGilDropIsUncertain()
    {
        Assert.Equal(ReceiptVerdict.Uncertain, ReceiptCorrelation.Correlate(Facts(GilBefore - Deposit + 1, RecordReading(12, 5), true)).Verdict);
        Assert.Equal(ReceiptVerdict.Uncertain, ReceiptCorrelation.Correlate(Facts(null, RecordReading(12, 5), true)).Verdict);
    }

    [Fact]
    public void RefusalWithUntouchedGilIsARejection()
    {
        var result = ReceiptCorrelation.Correlate(Facts(GilBefore, null, null) with { Rejection = HousingText.OneEntryPerPeriod });

        Assert.Equal(ReceiptVerdict.Rejected, result.Verdict);
        Assert.Equal(HousingText.OneEntryPerPeriod, result.Rejection);
    }

    [Fact]
    public void RefusalAfterTheGilMovedIsStillUncertain()
    {
        var result = ReceiptCorrelation.Correlate(Facts(GilBefore - Deposit, null, null) with { Rejection = HousingText.OneEntryPerPeriod });

        Assert.Equal(ReceiptVerdict.Uncertain, result.Verdict);
    }

    [Fact]
    public void EntryElsewhereOrNoEntryWithUntouchedGilIsARejection()
    {
        Assert.Equal(ReceiptVerdict.Rejected, ReceiptCorrelation.Correlate(Facts(GilBefore, RecordReading(3, 1), false)).Verdict);
        Assert.Equal(ReceiptVerdict.Rejected, ReceiptCorrelation.Correlate(Facts(GilBefore, Reading(LotterySnapshotState.LoadedNone), null)).Verdict);
        Assert.Equal(ReceiptVerdict.Uncertain, ReceiptCorrelation.Correlate(Facts(GilBefore, null, null)).Verdict);
    }

    [Fact]
    public void PendingAttemptIsConfirmedOnlyByAnEntryOnItsPlot()
    {
        var attempt = Attempt(Plot(12, 5), AttemptState.NeedsReconciliation);

        Assert.Equal(ReconciliationVerdict.Confirmed, ReconciliationRules.Decide(Reconcile(attempt, RecordReading(12, 5), true)));
        Assert.Equal(ReconciliationVerdict.Rejected, ReconciliationRules.Decide(Reconcile(attempt, RecordReading(3, 1), false)));
        Assert.Equal(ReconciliationVerdict.Rejected, ReconciliationRules.Decide(Reconcile(attempt, Reading(LotterySnapshotState.LoadedNone), null)));
    }

    [Fact]
    public void UnclearReadingKeepsTheAttemptUncertain()
    {
        var attempt = Attempt(Plot(12, 5), AttemptState.NeedsReconciliation);

        Assert.Equal(ReconciliationVerdict.StillUncertain, ReconciliationRules.Decide(Reconcile(attempt, Reading(LotterySnapshotState.Unloaded), null)));
        Assert.Equal(ReconciliationVerdict.StillUncertain, ReconciliationRules.Decide(Reconcile(attempt, TextReading(null), null)));
        Assert.Equal(ReconciliationVerdict.StillUncertain, ReconciliationRules.Decide(Reconcile(attempt, RecordReading(12, 5), true) with { ReadingInCyclePhase = false }));
    }

    [Fact]
    public void ReadingOfAnotherCycleOrCharacterDecidesNothing()
    {
        var attempt = Attempt(Plot(12, 5), AttemptState.NeedsReconciliation);

        Assert.Equal(ReconciliationVerdict.StillUncertain, ReconciliationRules.Decide(Reconcile(attempt, RecordReading(12, 5), true) with { Position = VerifiedEntryPhase() with { Cycle = SecondCycle } }));
        Assert.Equal(ReconciliationVerdict.StillUncertain, ReconciliationRules.Decide(Reconcile(attempt, RecordReading(12, 5, contentId: ContentId + 1), true)));
    }

    [Fact]
    public void SettledAttemptsAreNotReadBack()
    {
        Assert.Equal(ReconciliationVerdict.StillUncertain, ReconciliationRules.Decide(Reconcile(Attempt(Plot(12, 5), AttemptState.Confirmed), RecordReading(12, 5), true)));
        Assert.Equal(ReconciliationVerdict.StillUncertain, ReconciliationRules.Decide(Reconcile(Attempt(Plot(12, 5), AttemptState.Rejected), Reading(LotterySnapshotState.LoadedNone), null)));
    }

    private static ReceiptFacts Facts(long? gilAfter, LotterySnapshot? reading, bool? namesPlot)
        => new(Deposit, GilBefore, gilAfter, null, reading, namesPlot);

    private static ReconciliationFacts Reconcile(ActionAttempt attempt, LotterySnapshot reading, bool? namesPlot)
        => new(attempt, VerifiedEntryPhase(), reading, namesPlot, true);
}
