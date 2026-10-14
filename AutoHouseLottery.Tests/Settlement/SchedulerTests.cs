using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Settlement.Model;
using static AutoHouseLottery.Tests.HousingFixtures;
using static AutoHouseLottery.Tests.Integration.IntegrationFixtures;

namespace AutoHouseLottery.Tests.Settlement;

public sealed class SchedulerTests
{
    private const long Reserve = 100_000;
    private const long Rich = 10_000_000;

    [Fact]
    public void BackgroundWorkWaitsForThirtySecondsOfIdle()
    {
        Assert.Equal(SchedulerBlock.NotIdle, SchedulerRules.Check(Ready() with { IdleMs = SchedulerRules.IdleMsRequired - 1 }));
        Assert.Equal(SchedulerBlock.None, SchedulerRules.Check(Ready() with { IdleMs = SchedulerRules.IdleMsRequired }));
    }

    [Fact]
    public void CombatDutyCutsceneUiAndOtherWorkDeferIt()
    {
        Assert.Equal(SchedulerBlock.InDuty, SchedulerRules.Check(Ready() with { InDuty = true, InCombat = true }));
        Assert.Equal(SchedulerBlock.InCombat, SchedulerRules.Check(Ready() with { InCombat = true }));
        Assert.Equal(SchedulerBlock.InCutscene, SchedulerRules.Check(Ready() with { InCutscene = true }));
        Assert.Equal(SchedulerBlock.Busy, SchedulerRules.Check(Ready() with { Busy = true }));
        Assert.Equal(SchedulerBlock.Occupied, SchedulerRules.Check(Ready() with { Occupied = true }));
        Assert.Equal(SchedulerBlock.Typing, SchedulerRules.Check(Ready() with { Typing = true }));
    }

    [Fact]
    public void NothingRunsLoggedOutWithRefundsOffOrWithNothingDue()
    {
        Assert.Equal(SchedulerBlock.NotLoggedIn, SchedulerRules.Check(Ready() with { LoggedIn = false }));
        Assert.Equal(SchedulerBlock.RefundsOff, SchedulerRules.Check(Ready() with { AutomaticRefunds = false }));
        Assert.Equal(SchedulerBlock.NothingDue, SchedulerRules.Check(Ready() with { WorkDue = false }));
        Assert.Equal(SchedulerBlock.JournalUnavailable, SchedulerRules.Check(Ready() with { JournalWritable = false }));
    }

    [Fact]
    public void TheSessionTravelCapAndTheReserveBoundTravel()
    {
        Assert.Equal(SchedulerRules.SessionTravelCap, SchedulerRules.TravelAllowance(0, Rich, Reserve));
        Assert.Equal(SchedulerRules.SessionTravelCap - 9_500, SchedulerRules.TravelAllowance(9_500, Rich, Reserve));
        Assert.Equal(0, SchedulerRules.TravelAllowance(12_000, Rich, Reserve));
        Assert.Equal(2_000, SchedulerRules.TravelAllowance(0, Reserve + 2_000, Reserve));
        Assert.Equal(0, SchedulerRules.TravelAllowance(0, Reserve - 1, Reserve));
        Assert.Equal(0, SchedulerRules.TravelAllowance(0, null, Reserve));
    }

    [Fact]
    public void AnExhaustedBudgetOrReserveDefersWithItsOwnReason()
    {
        Assert.Equal(SchedulerBlock.TravelBudgetSpent, SchedulerRules.Check(Ready() with { TravelAllowance = SchedulerRules.TravelLegEstimate - 1 }));
        Assert.Equal(SchedulerBlock.BelowReserve, SchedulerRules.Check(Ready() with { Gil = Reserve }));
        Assert.Equal(SchedulerBlock.BelowReserve, SchedulerRules.Check(Ready() with { Gil = null }));
    }

    [Fact]
    public void WorkThatCannotStartIsAnnouncedAfterAMinuteExceptByChoice()
    {
        Assert.True(SchedulerRules.ShouldAnnounceReady(SchedulerBlock.InDuty, SchedulerRules.ReadyNoticeDelayMs, ownVisitRunning: false));
        Assert.True(SchedulerRules.ShouldAnnounceReady(SchedulerBlock.NotIdle, SchedulerRules.ReadyNoticeDelayMs, ownVisitRunning: false));
        Assert.False(SchedulerRules.ShouldAnnounceReady(SchedulerBlock.InDuty, SchedulerRules.ReadyNoticeDelayMs - 1, ownVisitRunning: false));
        Assert.False(SchedulerRules.ShouldAnnounceReady(SchedulerBlock.RefundsOff, SchedulerRules.ReadyNoticeDelayMs, ownVisitRunning: false));
        Assert.False(SchedulerRules.ShouldAnnounceReady(SchedulerBlock.None, SchedulerRules.ReadyNoticeDelayMs, ownVisitRunning: false));
    }

    [Fact]
    public void ItsOwnVisitIsNeverAnnouncedAsWorkThatCannotStart()
    {
        Assert.False(SchedulerRules.ShouldAnnounceReady(SchedulerBlock.Busy, SchedulerRules.ReadyNoticeDelayMs * 10, ownVisitRunning: true));
    }

    [Fact]
    public void APluginThatAnswersDialogsHoldsEveryVisit()
    {
        Assert.Equal(SchedulerBlock.AutoAnswerPlugin, SchedulerRules.Check(Ready() with { AutoAnswerLoaded = true }));
        Assert.True(SchedulerRules.ShouldAnnounceReady(SchedulerBlock.AutoAnswerPlugin, SchedulerRules.ReadyNoticeDelayMs, ownVisitRunning: false));
    }

    [Fact]
    public void AGmAlertHoldsEveryVisitUntilThePlayersOwnInput()
    {
        const long HeldAtMs = 1_000_000;

        Assert.Equal(SchedulerBlock.HeldAfterAlert, SchedulerRules.Check(Ready() with { HeldAfterAlert = true }));
        Assert.Equal(SchedulerBlock.HeldAfterAlert, SchedulerRules.Check(Ready() with { HeldAfterAlert = true, IdleMs = SchedulerRules.IdleMsRequired * 100 }));
        Assert.False(SchedulerRules.HoldReleased(HeldAtMs, idleMs: 5_000, nowMs: HeldAtMs + 5_000));
        Assert.False(SchedulerRules.HoldReleased(HeldAtMs, idleMs: SchedulerRules.IdleMsRequired * 100, nowMs: HeldAtMs + SchedulerRules.IdleMsRequired * 10));
        Assert.True(SchedulerRules.HoldReleased(HeldAtMs, idleMs: 1_000, nowMs: HeldAtMs + 5_000));
    }

    [Fact]
    public void ResultsArrivingScheduleOneCheckPerUnknownEntry()
    {
        var entered = Receipt(Plot(12, 5), FirstCycle);
        var journal = Journal([], (entered, SettlementState.Entered));

        Assert.Empty(SettlementSchedule.Due(journal, ContentId, VerifiedEntryPhase(), (_, _) => true, _ => false));
        Assert.Equal([new SettlementWork(entered.ReceiptId, SettlementMode.Refund)], SettlementSchedule.Due(journal, ContentId, ResultsPhase(FirstCycle), (_, _) => true, _ => false).AsSpan().ToArray());
        Assert.Empty(SettlementSchedule.Due(journal, ContentId + 1, ResultsPhase(FirstCycle), (_, _) => true, _ => false));
    }

    [Fact]
    public void APublicPhaseChangeAloneNeverSettlesAnything()
    {
        var entered = Receipt(Plot(12, 5), FirstCycle);
        var journal = Journal([], (entered, SettlementState.Entered));

        var due = SettlementSchedule.Due(journal, ContentId, ResultsPhase(SecondCycle), (_, _) => true, _ => false);

        Assert.Equal(SettlementMode.Refund, Assert.Single(due).Mode);
    }

    [Fact]
    public void WinsWaitForThePlayerAndKnownRefundsWaitForAPermittedPress()
    {
        var won = Receipt(Plot(12, 5), FirstCycle);
        var lost = Receipt(Plot(12, 6), FirstCycle);
        var settled = Receipt(Plot(12, 7), FirstCycle);
        var journal = Journal([], (won, SettlementState.WonUnclaimed), (lost, SettlementState.LostRefundDue), (settled, SettlementState.Refunded));

        Assert.Empty(SettlementSchedule.Due(journal, ContentId, ResultsPhase(FirstCycle), (_, _) => false, _ => false));
        Assert.Equal([new SettlementWork(lost.ReceiptId, SettlementMode.Refund)], SettlementSchedule.Due(journal, ContentId, ResultsPhase(FirstCycle), (receiptId, plot) => receiptId == lost.ReceiptId && plot == lost.Plot, _ => false).AsSpan().ToArray());
    }

    [Fact]
    public void ARefundInQuestionIsOnlyReadBackNeverPressedAgain()
    {
        var receipt = Receipt(Plot(12, 5), FirstCycle);
        var open = Attempt(Plot(12, 5), AttemptState.NeedsReconciliation) with { Kind = ActionKind.Refund, ReceiptId = receipt.ReceiptId };
        var journal = Journal([open], (receipt, SettlementState.NeedsReconciliation));

        Assert.Equal([new SettlementWork(receipt.ReceiptId, SettlementMode.Inspect)], SettlementSchedule.Due(journal, ContentId, ResultsPhase(FirstCycle), (_, _) => true, _ => false).AsSpan().ToArray());
    }

    [Fact]
    public void TwoUnresolvedHistoricalEntriesKeepTheirOwnIdentities()
    {
        var older = Receipt(Plot(12, 5), FirstCycle);
        var newer = Receipt(Plot(3, 40), SecondCycle);
        var journal = Journal([], (older, SettlementState.Entered), (newer, SettlementState.OutcomeUnknown));

        var due = SettlementSchedule.Due(journal, ContentId, ResultsPhase(SecondCycle), (_, _) => true, _ => false);

        Assert.Equal([new SettlementWork(older.ReceiptId, SettlementMode.Refund), new SettlementWork(newer.ReceiptId, SettlementMode.Refund)], due.AsSpan().ToArray());
    }

    [Fact]
    public void AnEarlierCyclesUncertainAttemptIsSettledOnlyThroughItsPlotsResult()
    {
        var uncertain = Attempt(Plot(12, 5), AttemptState.NeedsReconciliation);
        var current = Attempt(Plot(12, 6), AttemptState.NeedsReconciliation, SecondCycle);
        var journal = Journal([uncertain, current]);

        var due = SettlementSchedule.Due(journal, ContentId, VerifiedEntryPhase() with { Cycle = SecondCycle }, (_, _) => true, _ => false);

        Assert.Equal([new SettlementWork(uncertain.AttemptId, SettlementMode.Refund)], due.AsSpan().ToArray());
    }

    [Fact]
    public void AnEntryInBothTheResultChecksAndThisCycleIsQueuedOnce()
    {
        var older = Receipt(Plot(12, 5), FirstCycle);
        var refund = Attempt(Plot(12, 5), AttemptState.NeedsReconciliation) with { Kind = ActionKind.Refund, ReceiptId = older.ReceiptId };
        var journal = Journal([refund], (older, SettlementState.NeedsReconciliation));

        var due = SettlementSchedule.Due(journal, ContentId, ResultsPhase(SecondCycle), (_, _) => true, _ => false);

        Assert.Equal([new SettlementWork(older.ReceiptId, SettlementMode.Inspect)], due.AsSpan().ToArray());
    }

    [Fact]
    public void AParkedEntryIsLeftForThePlayersOwnCheck()
    {
        var parked = Receipt(Plot(12, 5), FirstCycle);
        var open = Receipt(Plot(12, 6), FirstCycle);
        var uncertain = Attempt(Plot(12, 7), AttemptState.NeedsReconciliation);
        var journal = Journal([uncertain], (parked, SettlementState.Entered), (open, SettlementState.Entered));

        var due = SettlementSchedule.Due(journal, ContentId, ResultsPhase(SecondCycle), (_, _) => true, id => id == parked.ReceiptId || id == uncertain.AttemptId);

        Assert.Equal([new SettlementWork(open.ReceiptId, SettlementMode.Refund)], due.AsSpan().ToArray());
    }

    [Fact]
    public void EachWorkRunsAtMostTwiceASessionTenMinutesApart()
    {
        var queue = new SettlementQueue();
        var work = new SettlementWork(Guid.NewGuid(), SettlementMode.Refund);
        var due = System.Collections.Immutable.ImmutableArray.Create(work);

        Assert.Equal(work, queue.Next(due, 0));
        queue.Started(work, 0);
        Assert.Null(queue.Next(due, SettlementQueue.RetryDelayMs - 1));
        Assert.Equal(work, queue.Next(due, SettlementQueue.RetryDelayMs));
        queue.Started(work, SettlementQueue.RetryDelayMs);
        Assert.Null(queue.Next(due, long.MaxValue));
    }

    [Fact]
    public void WorkPausedForThePlayerKeepsItsTurn()
    {
        var queue = new SettlementQueue();
        var work = new SettlementWork(Guid.NewGuid(), SettlementMode.Refund);
        var due = System.Collections.Immutable.ImmutableArray.Create(work);

        queue.Started(work, 0);
        queue.GiveBack(work);

        Assert.Equal(work, queue.Next(due, 1));
    }

    private static SchedulerFacts Ready() => new(
        LoggedIn: true,
        AutomaticRefunds: true,
        WorkDue: true,
        JournalWritable: true,
        AutoAnswerLoaded: false,
        HeldAfterAlert: false,
        InDuty: false,
        InCombat: false,
        InCutscene: false,
        Busy: false,
        Occupied: false,
        Typing: false,
        IdleMs: SchedulerRules.IdleMsRequired,
        TravelAllowance: SchedulerRules.SessionTravelCap,
        Gil: Rich,
        Reserve: Reserve);
}
