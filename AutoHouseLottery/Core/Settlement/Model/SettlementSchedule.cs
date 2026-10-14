using System.Collections.Immutable;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Shared;

namespace AutoHouseLottery.Core.Settlement.Model;

internal enum SchedulerBlock : byte
{
    None,
    NotLoggedIn,
    RefundsOff,
    NothingDue,
    JournalUnavailable,
    InDuty,
    InCombat,
    InCutscene,
    Busy,
    Occupied,
    Typing,
    NotIdle,
    BelowReserve,
    TravelBudgetSpent,
}

// What the scheduler checks once a second before background work. IdleMs is the time since the player's own last input;
// TravelAllowance is what this session may still spend travelling; Gil is the live balance, null when unreadable.
internal readonly record struct SchedulerFacts(
    bool LoggedIn,
    bool AutomaticRefunds,
    bool WorkDue,
    bool JournalWritable,
    bool InDuty,
    bool InCombat,
    bool InCutscene,
    bool Busy,
    bool Occupied,
    bool Typing,
    long IdleMs,
    long TravelAllowance,
    long? Gil,
    long Reserve);

internal static class SchedulerRules
{
    // Proposed defaults from TECHNICAL_DESIGN section 12, not game rules.
    public const int IdleMsRequired = 30_000;
    public const long SessionTravelCap = 10_000;

    // An estimate of what one housing trip can cost (a teleport and the aethernet); a session with less left does not set
    // out, so a trip is never started that the budget cannot cover.
    public const long TravelLegEstimate = 1_000;

    public const int ReadyNoticeDelayMs = 60_000;

    public static SchedulerBlock Check(in SchedulerFacts facts)
    {
        if (!facts.LoggedIn)
        {
            return SchedulerBlock.NotLoggedIn;
        }

        if (!facts.AutomaticRefunds)
        {
            return SchedulerBlock.RefundsOff;
        }

        if (!facts.WorkDue)
        {
            return SchedulerBlock.NothingDue;
        }

        if (!facts.JournalWritable)
        {
            return SchedulerBlock.JournalUnavailable;
        }

        if (facts.InDuty)
        {
            return SchedulerBlock.InDuty;
        }

        if (facts.InCombat)
        {
            return SchedulerBlock.InCombat;
        }

        if (facts.InCutscene)
        {
            return SchedulerBlock.InCutscene;
        }

        if (facts.Busy)
        {
            return SchedulerBlock.Busy;
        }

        if (facts.Occupied)
        {
            return SchedulerBlock.Occupied;
        }

        if (facts.Typing)
        {
            return SchedulerBlock.Typing;
        }

        if (facts.IdleMs < IdleMsRequired)
        {
            return SchedulerBlock.NotIdle;
        }

        if (facts.Gil is not { } gil || gil <= facts.Reserve)
        {
            return SchedulerBlock.BelowReserve;
        }

        return facts.TravelAllowance < TravelLegEstimate ? SchedulerBlock.TravelBudgetSpent : SchedulerBlock.None;
    }

    // The smaller of what is left of the session cap and what the character can spend above its reserve. An unreadable
    // balance allows nothing.
    public static long TravelAllowance(long spentThisSession, long? gil, long reserve)
    {
        if (gil is not { } balance
            || !Gil.TrySubtract(SessionTravelCap, Math.Max(0, spentThisSession), out var capLeft)
            || !Gil.TrySubtract(balance, Math.Max(0, reserve), out var aboveReserve))
        {
            return 0;
        }

        return Math.Max(0, Math.Min(capLeft, aboveReserve));
    }

    // Work that has waited a minute for anything but the player's own choices is announced, so it is never invisible.
    public static bool ShouldAnnounceReady(SchedulerBlock block, long blockedForMs)
        => block is not (SchedulerBlock.None or SchedulerBlock.NotLoggedIn or SchedulerBlock.RefundsOff or SchedulerBlock.NothingDue)
        && blockedForMs >= ReadyNoticeDelayMs;
}

internal static class SettlementSchedule
{
    // The work the scheduler may do for one character: a result check, collecting when it is a refund, for every entry
    // whose results have arrived and whose outcome is unknown; a collection for a refund already known to be owed once it
    // can actually be collected (a refund that could only be inspected again teaches nothing); and a read-back for a
    // refund of this plugin that may already have gone through. Wins wait for the player.
    public static ImmutableArray<SettlementWork> Due(in JournalSnapshot snapshot, ulong contentId, in CyclePosition current, Func<PlotKey, bool> collectable)
    {
        var due = ImmutableArray.CreateBuilder<SettlementWork>();
        var receipts = snapshot.Receipts;
        for (var index = 0; index < receipts.Length; index++)
        {
            var receipt = receipts[index];
            if (receipt.CharacterContentId != contentId
                || !SettlementDeadlines.ResultsArrived(receipt.Cycle, current)
                || SettlementLookups.Settlement(snapshot, receipt.ReceiptId) is not { } settlement)
            {
                continue;
            }

            var open = SettlementLookups.OpenAttempt(snapshot, receipt.ReceiptId);
            if (open is not null)
            {
                if (open.Kind == ActionKind.Refund)
                {
                    due.Add(new SettlementWork(receipt.ReceiptId, SettlementMode.Inspect));
                }

                continue;
            }

            if (SettlementStates.OutcomeOpen(settlement.State)
                || (SettlementStates.RefundOwed(settlement.State) && receipt.Plot is { } plot && collectable(plot)))
            {
                due.Add(new SettlementWork(receipt.ReceiptId, SettlementMode.Refund));
            }
        }

        return due.ToImmutable();
    }
}

// The session's record of what the scheduler started, so each piece of work runs at most twice a session (one retry,
// ten minutes apart) and the same receipt is never queued twice. It is rebuilt empty after a reload, when the journal
// itself says again what is due, so a reload schedules each check once more and not twice.
internal sealed class SettlementQueue
{
    public const int MaxAttemptsPerSession = 2;
    public const long RetryDelayMs = 600_000;

    private readonly Dictionary<SettlementWork, Tries> tries = [];

    public SettlementWork? Next(ImmutableArray<SettlementWork> due, long nowMs)
    {
        for (var index = 0; index < due.Length; index++)
        {
            var work = due[index];
            if (!tries.TryGetValue(work, out var record) || (record.Attempts < MaxAttemptsPerSession && nowMs >= record.NextMs))
            {
                return work;
            }
        }

        return null;
    }

    public void Started(SettlementWork work, long nowMs)
    {
        var attempts = tries.TryGetValue(work, out var record) ? record.Attempts : 0;
        tries[work] = new Tries(attempts + 1, nowMs + RetryDelayMs);
    }

    // Work paused because the player came back did not get its turn, so it does not count as an attempt.
    public void GiveBack(SettlementWork work)
    {
        if (!tries.TryGetValue(work, out var record))
        {
            return;
        }

        if (record.Attempts <= 1)
        {
            tries.Remove(work);
            return;
        }

        tries[work] = record with { Attempts = record.Attempts - 1, NextMs = 0 };
    }

    public void Clear() => tries.Clear();

    private readonly record struct Tries(int Attempts, long NextMs);
}
