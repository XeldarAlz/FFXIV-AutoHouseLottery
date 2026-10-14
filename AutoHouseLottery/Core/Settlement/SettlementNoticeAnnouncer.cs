using AutoHouseLottery.Core.Integration;
using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Core.Settlement.Model;
using Dalamud.Interface.ImGuiNotification;
using ECommons.DalamudServices;

namespace AutoHouseLottery.Core.Settlement;

// The results, claim-deadline and refund-deadline notices (UX section 12), worked out once a minute from the journal and
// recorded in the settings once shown, so each fires once per entry, including when the plugin catches up after a login.
// Beyond a few at once they fold into one summary.
internal sealed class SettlementNoticeAnnouncer(HousingContext housing, Configuration configuration)
{
    private const long CheckIntervalMs = TimeUnits.MillisecondsPerMinute;
    private const int MaximumSeparate = 3;
    private const string PluginTitle = "Auto House Lottery";
    private const string DateFormat = "g";

    private long nextCheckMs;

    public void Tick(long nowMs)
    {
        if (nowMs < nextCheckMs)
        {
            return;
        }

        nextCheckMs = nowMs + CheckIntervalMs;
        var now = DateTime.UtcNow;
        var due = SettlementNotices.Due(housing.JournalSnapshot, housing.CurrentPosition(now), housing.Schedule, now, configuration.SettlementNoticeDelivered);
        if (due.IsEmpty)
        {
            return;
        }

        for (var index = 0; index < due.Length; index++)
        {
            var notice = due[index];
            configuration.RecordSettlementNotice(notice.Key, now);
            RunLog.Info($"Settlement notice: {notice.Kind} for receipt {notice.ReceiptId} ({notice.Plot?.ToString() ?? notice.AddressText ?? "plot not named"}), deadline {notice.DeadlineUtc?.ToString("u") ?? "unknown"}.");
            if (due.Length <= MaximumSeparate)
            {
                Show(PluginTitle, Describe(notice));
            }
        }

        if (due.Length > MaximumSeparate)
        {
            Show(Loc.Plural(L.Settlement.NoticeSummary, due.Length), Loc.T(L.Settlement.NoticeSummaryBody));
        }
    }

    public static void AnnounceReady(int dueCount) => Show(Loc.T(L.Settlement.ReadyTitle), Loc.Plural(L.Settlement.ReadyBody, dueCount));

    private string Describe(in SettlementNotice notice)
    {
        var address = notice.Plot is { } plot ? PlotNames.Address(plot) : notice.AddressText ?? Loc.T(L.Lottery.PlotNotNamed);
        var character = configuration.Characters.TryGetValue(notice.ContentId, out var known) && known.Name.Length > 0 ? known.Name : Loc.T(L.Lottery.UnknownCharacter);
        var deadline = notice.DeadlineUtc is { } at ? at.ToLocalTime().ToString(DateFormat, Loc.Culture) : Loc.T(L.Lottery.DeadlineUnknown);
        return notice.Kind switch
        {
            SettlementNoticeKind.Won => Loc.T(L.Settlement.NoticeWon, address, character, deadline),
            SettlementNoticeKind.ClaimDeadline => Loc.T(L.Settlement.NoticeClaimDeadline, address, character, deadline),
            SettlementNoticeKind.RefundDeadline => Loc.T(L.Settlement.NoticeRefundDeadline, address, character, deadline),
            _ => Loc.T(L.Settlement.NoticeResults, address, character),
        };
    }

    private static void Show(string title, string content)
    {
        Svc.NotificationManager.AddNotification(new Notification
        {
            Title = title,
            Content = content,
            Type = NotificationType.Info,
        });
    }
}
