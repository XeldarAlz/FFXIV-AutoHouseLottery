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
        Assert.True(SchedulerRules.ShouldAnnounceReady(SchedulerBlock.InDuty, SchedulerRules.ReadyNoticeDelayMs));
        Assert.True(SchedulerRules.ShouldAnnounceReady(SchedulerBlock.NotIdle, SchedulerRules.ReadyNoticeDelayMs));
        Assert.False(SchedulerRules.ShouldAnnounceReady(SchedulerBlock.InDuty, SchedulerRules.ReadyNoticeDelayMs - 1));
        Assert.False(SchedulerRules.ShouldAnnounceReady(SchedulerBlock.RefundsOff, SchedulerRules.ReadyNoticeDelayMs));
        Assert.False(SchedulerRules.ShouldAnnounceReady(SchedulerBlock.None, SchedulerRules.ReadyNoticeDelayMs));
    }

    [Fact]
    public void ResultsArrivingScheduleOneCheckPerUnknownEntry()
    {
        var entered = Receipt(Plot(12, 5), FirstCycle);
        var journal = Journal([], (entered, SettlementState.Entered));

        Assert.Empty(SettlementSchedule.Due(journal, ContentId, VerifiedEntryPhase(), _ => true));
        Assert.Equal([new SettlementWork(entered.ReceiptId, SettlementMode.Refund)], SettlementSchedule.Due(journal, ContentId, ResultsPhase(FirstCycle), _ => true).AsSpan().ToArray());
        Assert.Empty(SettlementSchedule.Due(journal, ContentId + 1, ResultsPhase(FirstCycle), _ => true));
    }

    [Fact]
    public void APublicPhaseChangeAloneNeverSettlesAnything()
    {
        var entered = Receipt(Plot(12, 5), FirstCycle);
        var journal = Journal([], (entered, SettlementState.Entered));

        var due = SettlementSchedule.Due(journal, ContentId, ResultsPhase(SecondCycle), _ => true);

        Assert.Equal(SettlementMode.Refund, Assert.Single(due).Mode);
    }

    [Fact]
    public void WinsWaitForThePlayerAndKnownRefundsWaitForAPermittedPress()
    {
        var won = Receipt(Plot(12, 5), FirstCycle);
        var lost = Receipt(Plot(12, 6), FirstCycle);
        var settled = Receipt(Plot(12, 7), FirstCycle);
        var journal = Journal([], (won, SettlementState.WonUnclaimed), (lost, SettlementState.LostRefundDue), (settled, SettlementState.Refunded));

        Assert.Empty(SettlementSchedule.Due(journal, ContentId, ResultsPhase(FirstCycle), _ => false));
        Assert.Equal([new SettlementWork(lost.ReceiptId, SettlementMode.Refund)], SettlementSchedule.Due(journal, ContentId, ResultsPhase(FirstCycle), plot => plot == lost.Plot).AsSpan().ToArray());
    }

    [Fact]
    public void ARefundInQuestionIsOnlyReadBackNeverPressedAgain()
    {
        var receipt = Receipt(Plot(12, 5), FirstCycle);
        var open = Attempt(Plot(12, 5), AttemptState.NeedsReconciliation) with { Kind = ActionKind.Refund, ReceiptId = receipt.ReceiptId };
        var journal = Journal([open], (receipt, SettlementState.NeedsReconciliation));

        Assert.Equal([new SettlementWork(receipt.ReceiptId, SettlementMode.Inspect)], SettlementSchedule.Due(journal, ContentId, ResultsPhase(FirstCycle), _ => true).AsSpan().ToArray());
    }

    [Fact]
    public void TwoUnresolvedHistoricalEntriesKeepTheirOwnIdentities()
    {
        var older = Receipt(Plot(12, 5), FirstCycle);
        var newer = Receipt(Plot(3, 40), SecondCycle);
        var journal = Journal([], (older, SettlementState.Entered), (newer, SettlementState.OutcomeUnknown));

        var due = SettlementSchedule.Due(journal, ContentId, ResultsPhase(SecondCycle), _ => true);

        Assert.Equal([new SettlementWork(older.ReceiptId, SettlementMode.Refund), new SettlementWork(newer.ReceiptId, SettlementMode.Refund)], due.AsSpan().ToArray());
    }

    [Fact]
    public void AnEarlierCyclesUncertainAttemptIsSettledOnlyThroughItsPlotsResult()
    {
        var uncertain = Attempt(Plot(12, 5), AttemptState.NeedsReconciliation);
        var current = Attempt(Plot(12, 6), AttemptState.NeedsReconciliation, SecondCycle);
        var journal = Journal([uncertain, current]);

        var due = SettlementSchedule.Due(journal, ContentId, VerifiedEntryPhase() with { Cycle = SecondCycle }, _ => true);

        Assert.Equal([new SettlementWork(uncertain.AttemptId, SettlementMode.Refund)], due.AsSpan().ToArray());
    }

    [Fact]
    public void AnEntryInBothTheResultChecksAndThisCycleIsQueuedOnce()
    {
        var older = Receipt(Plot(12, 5), FirstCycle);
        var refund = Attempt(Plot(12, 5), AttemptState.NeedsReconciliation) with { Kind = ActionKind.Refund, ReceiptId = older.ReceiptId };
        var journal = Journal([refund], (older, SettlementState.NeedsReconciliation));

        var due = SettlementSchedule.Due(journal, ContentId, ResultsPhase(SecondCycle), _ => true);

        Assert.Equal([new SettlementWork(older.ReceiptId, SettlementMode.Inspect)], due.AsSpan().ToArray());
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
