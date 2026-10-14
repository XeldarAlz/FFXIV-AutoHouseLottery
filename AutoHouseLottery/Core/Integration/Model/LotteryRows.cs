using System.Collections.Immutable;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence.Model;
using LotteryPhase = AutoHouseLottery.Core.Housing.Model.LotteryPhase;

namespace AutoHouseLottery.Core.Integration.Model;

internal enum LotterySection : byte { ActionNeeded, Active, History }

internal enum LotteryRowState : byte { Entered, ResultsAvailable, Won, RefundDue, SettlementPending, Uncertain, Settled, Rejected }

internal enum LotteryAction : byte { None, ViewPlot, CheckResult, ClaimHouse, CollectRefund, VerifyOutcome, ViewReceipt }

// One entry on My lottery. Deposit, entry number, plot and dates stay null wherever the record does not hold them.
internal sealed record LotteryRow(
    Guid Id,
    ulong ContentId,
    CycleKey Cycle,
    PlotKey? Plot,
    string? AddressText,
    int? EntryNumber,
    long? Deposit,
    DateTime? EnteredUtc,
    DateTime? DeadlineUtc,
    LotteryRowState State,
    LotterySection Section,
    LotteryAction Action,
    ReceiptEvidence? Evidence)
{
    public bool Imported => Evidence == ReceiptEvidence.ImportedFromGame;
}

internal static class LotteryRows
{
    public static ImmutableArray<LotteryRow> Build(in JournalSnapshot snapshot, in CyclePosition current, LotterySchedule? schedule)
    {
        var rows = new List<LotteryRow>(snapshot.Receipts.Length + snapshot.Attempts.Length);
        var receipts = snapshot.Receipts;
        for (var index = 0; index < receipts.Length; index++)
        {
            var receipt = receipts[index];
            if (JournalLookups.TryFindSettlement(snapshot, receipt.ReceiptId, out var settlement))
            {
                rows.Add(ForReceipt(receipt, settlement, current, schedule));
            }
        }

        var attempts = snapshot.Attempts;
        for (var index = 0; index < attempts.Length; index++)
        {
            var attempt = attempts[index];
            if (attempt.Kind == ActionKind.Entry && attempt.State != AttemptState.Confirmed)
            {
                rows.Add(ForAttempt(attempt));
            }
        }

        rows.Sort(Compare);
        return [.. rows];
    }

    private static LotteryRow ForReceipt(EntryReceipt receipt, in EntrySettlement settlement, in CyclePosition current, LotterySchedule? schedule)
    {
        var (state, section, action) = Classify(settlement.State, receipt.Cycle, current);
        return new LotteryRow(
            receipt.ReceiptId,
            receipt.CharacterContentId,
            receipt.Cycle,
            receipt.Plot,
            receipt.AddressText,
            receipt.EntryNumber,
            receipt.Deposit,
            receipt.EnteredUtc,
            DeadlineOf(state, receipt.Cycle, current, schedule),
            state,
            section,
            action,
            receipt.Evidence);
    }

    private static LotteryRow ForAttempt(ActionAttempt attempt)
    {
        var rejected = attempt.State == AttemptState.Rejected;
        return new LotteryRow(
            attempt.AttemptId,
            attempt.CharacterContentId,
            attempt.Cycle,
            attempt.Plot,
            null,
            null,
            attempt.Amount,
            null,
            null,
            rejected ? LotteryRowState.Rejected : LotteryRowState.Uncertain,
            rejected ? LotterySection.History : LotterySection.ActionNeeded,
            rejected ? LotteryAction.None : LotteryAction.VerifyOutcome,
            null);
    }

    private static (LotteryRowState State, LotterySection Section, LotteryAction Action) Classify(SettlementState state, CycleKey entered, in CyclePosition current)
    {
        switch (state)
        {
            case SettlementState.Entered:
            case SettlementState.ResultsPending:
                return entered == current.Cycle && current.Phase == LotteryPhase.Entry
                    ? (LotteryRowState.Entered, LotterySection.Active, LotteryAction.ViewPlot)
                    : (LotteryRowState.ResultsAvailable, LotterySection.ActionNeeded, LotteryAction.CheckResult);
            case SettlementState.InspectingResult:
            case SettlementState.OutcomeUnknown:
                return (LotteryRowState.ResultsAvailable, LotterySection.ActionNeeded, LotteryAction.CheckResult);
            case SettlementState.WonUnclaimed:
                return (LotteryRowState.Won, LotterySection.ActionNeeded, LotteryAction.ClaimHouse);
            case SettlementState.LostRefundDue:
            case SettlementState.ForfeitRefundDue:
                return (LotteryRowState.RefundDue, LotterySection.ActionNeeded, LotteryAction.CollectRefund);
            case SettlementState.ClaimPending:
            case SettlementState.RefundPending:
            case SettlementState.NeedsReconciliation:
                return (LotteryRowState.SettlementPending, LotterySection.ActionNeeded, LotteryAction.VerifyOutcome);
            default:
                return (LotteryRowState.Settled, LotterySection.History, LotteryAction.ViewReceipt);
        }
    }

    // The entry phase's end while entries are open; afterwards the end of the results period, until which a result can
    // be read. Unknown when no calendar covers the cycle's region.
    private static DateTime? DeadlineOf(LotteryRowState state, CycleKey cycle, in CyclePosition current, LotterySchedule? schedule)
    {
        if (state is LotteryRowState.Settled or LotteryRowState.Rejected)
        {
            return null;
        }

        if (cycle == current.Cycle)
        {
            return current.PhaseEndUtc;
        }

        if (schedule is not { } calendar || calendar.Region != cycle.Region)
        {
            return null;
        }

        return LotteryCalendar.EntryStartOf(calendar, cycle.Number) + LotteryCalendar.CycleLength;
    }

    private static int Compare(LotteryRow left, LotteryRow right)
    {
        var comparison = left.Section.CompareTo(right.Section);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = left.ContentId.CompareTo(right.ContentId);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = right.Cycle.Number.CompareTo(left.Cycle.Number);
        return comparison != 0 ? comparison : left.Id.CompareTo(right.Id);
    }
}
