using System.Collections.Immutable;
using System.Globalization;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence.Model;

namespace AutoHouseLottery.Core.Settlement.Model;

internal enum SettlementNoticeKind : byte { ResultsAvailable, ClaimDeadline, RefundDeadline }

// Each notice belongs to one receipt, so it fires once for that entry however often the plugin reloads or catches up.
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

    // Results available: an entry whose results arrived and whose outcome nobody has read. Claim deadline: a win not yet
    // claimed within a day of the end of its results period. Refund deadline: a refund owed within a week of the end of
    // its refund window. A deadline that has passed, or that the calendar cannot place, is not announced.
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

            SettlementNotice? notice = settlement.State switch
            {
                _ when SettlementStates.OutcomeOpen(settlement.State) && SettlementDeadlines.ResultsArrived(receipt.Cycle, current)
                    => Notice(SettlementNoticeKind.ResultsAvailable, receipt, SettlementDeadlines.ResultsEnd(receipt.Cycle, current, schedule)),
                SettlementState.WonUnclaimed when Approaching(SettlementDeadlines.ResultsEnd(receipt.Cycle, current, schedule), claimLead, nowUtc) is { } claimBy
                    => Notice(SettlementNoticeKind.ClaimDeadline, receipt, claimBy),
                _ when SettlementStates.RefundOwed(settlement.State) && Approaching(SettlementDeadlines.RefundDeadline(receipt.Cycle, current, schedule), refundLead, nowUtc) is { } refundBy
                    => Notice(SettlementNoticeKind.RefundDeadline, receipt, refundBy),
                _ => null,
            };
            if (notice is { } found && !delivered(found.Key))
            {
                due.Add(found);
            }
        }

        return due.ToImmutable();
    }

    private static DateTime? Approaching(DateTime? deadline, TimeSpan lead, DateTime nowUtc)
        => deadline is { } end && end > nowUtc && end - nowUtc <= lead ? end : null;

    private static SettlementNotice Notice(SettlementNoticeKind kind, EntryReceipt receipt, DateTime? deadline)
        => new(kind, receipt.ReceiptId, receipt.CharacterContentId, receipt.Plot, receipt.AddressText, deadline);
}
