using System.Collections.Immutable;
using System.Globalization;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence.Model;

namespace AutoHouseLottery.Core.Settlement.Model;

internal enum SettlementNoticeKind : byte { ResultsAvailable, Won, ClaimDeadline, RefundDeadline }

// Each notice belongs to one receipt, so it fires once for that entry across reloads and catch-ups.
internal readonly record struct SettlementNotice(SettlementNoticeKind Kind, Guid ReceiptId, ulong ContentId, PlotKey? Plot, string? AddressText, DateTime? DeadlineUtc)
{
    public string Key => string.Create(CultureInfo.InvariantCulture, $"{Kind}:{ReceiptId:N}");
}

internal static class SettlementNotices
{
    public const int ClaimLeadHours = 24;
    public const int RefundLeadDays = 7;

    private static readonly TimeSpan claimLead = TimeSpan.FromHours(ClaimLeadHours);
    private static readonly TimeSpan refundLead = TimeSpan.FromDays(RefundLeadDays);

    // Results available: an entry whose results arrived and whose outcome nobody has read. Won: a win the plugin found,
    // announced once whoever found it, since a win is never settled in the background, and not at all once its claim
    // deadline has passed. Claim deadline: that win still not claimed within a day of the end of its results period.
    // Refund deadline: a refund owed within a week of its deadline, the dialog's own when it was read, otherwise the
    // 90-day estimate. A deadline that has passed, or that nothing can place, is not announced.
    public static ImmutableArray<SettlementNotice> Due(in JournalSnapshot snapshot, in CyclePosition current, LotterySchedule? schedule, DateTime nowUtc, Func<string, bool> delivered)
    {
        var due = ImmutableArray.CreateBuilder<SettlementNotice>();
        var receipts = snapshot.Receipts;
        for (var index = 0; index < receipts.Length; index++)
        {
            var receipt = receipts[index];
            if (receipt.Cycle.Region != current.Cycle.Region || SettlementLookups.Settlement(snapshot, receipt.ReceiptId) is not { } settlement)
            {
                continue;
            }

            if (SettlementStates.OutcomeOpen(settlement.State) && SettlementDeadlines.ResultsArrived(receipt.Cycle, current))
            {
                Offer(due, Notice(SettlementNoticeKind.ResultsAvailable, receipt, SettlementDeadlines.ResultsEnd(receipt.Cycle, current, schedule)), delivered);
            }
            else if (settlement.State == SettlementState.WonUnclaimed)
            {
                var claimBy = SettlementDeadlines.ResultsEnd(receipt.Cycle, current, schedule);
                if (claimBy is { } end && end <= nowUtc)
                {
                    continue;
                }

                var won = Notice(SettlementNoticeKind.Won, receipt, claimBy);
                if (!delivered(won.Key))
                {
                    due.Add(won);
                }
                else if (Approaching(claimBy, claimLead, nowUtc) is { } claimEnd)
                {
                    Offer(due, Notice(SettlementNoticeKind.ClaimDeadline, receipt, claimEnd), delivered);
                }
            }
            else if (SettlementStates.RefundOwed(settlement.State)
                && Approaching(settlement.RefundDeadlineUtc ?? SettlementDeadlines.RefundDeadline(receipt.Cycle, current, schedule), refundLead, nowUtc) is { } refundBy)
            {
                Offer(due, Notice(SettlementNoticeKind.RefundDeadline, receipt, refundBy), delivered);
            }
        }

        return due.ToImmutable();
    }

    private static void Offer(ImmutableArray<SettlementNotice>.Builder due, in SettlementNotice notice, Func<string, bool> delivered)
    {
        if (!delivered(notice.Key))
        {
            due.Add(notice);
        }
    }

    private static DateTime? Approaching(DateTime? deadline, TimeSpan lead, DateTime nowUtc)
        => deadline is { } end && end > nowUtc && end - nowUtc <= lead ? end : null;

    private static SettlementNotice Notice(SettlementNoticeKind kind, EntryReceipt receipt, DateTime? deadline)
        => new(kind, receipt.ReceiptId, receipt.CharacterContentId, receipt.Plot, receipt.AddressText, deadline);
}
