using AutoHouseLottery.Core.Find;
using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Core.Maps;
using AutoHouseLottery.Core.Saved.Model;
using Dalamud.Interface.ImGuiNotification;
using ECommons.DalamudServices;

namespace AutoHouseLottery.Core.Saved;

// Turns saved plot notices into Dalamud notifications. A catch-up after a login can bring many at once, so beyond a few
// they are folded into one summary and the Saved page lists them all.
internal sealed class NoticeAnnouncer
{
    private const int MaximumSeparate = 3;
    private const string PluginTitle = "Auto House Lottery";

    private readonly Configuration configuration;
    private readonly GameNames names;

    public NoticeAnnouncer(Configuration configuration, GameNames names)
    {
        this.configuration = configuration;
        this.names = names;
    }

    public void Announce(IReadOnlyList<Notice> notices)
    {
        if (!configuration.NotifySaved || notices.Count == 0)
        {
            return;
        }

        if (notices.Count > MaximumSeparate)
        {
            Show(Loc.Plural(L.Saved.NoticeSummary, notices.Count), Loc.T(L.Saved.NoticeSummaryBody));
            return;
        }

        for (var index = 0; index < notices.Count; index++)
        {
            Show(PluginTitle, Describe(names, notices[index].Kind, notices[index].Plot, notices[index].SourceName));
        }
    }

    public static string Describe(GameNames names, NoticeKind kind, Housing.Model.PlotKey plot, string source)
    {
        var address = PlotLabels.AddressOnWorld(names, plot);
        return kind switch
        {
            NoticeKind.NewMatch => Loc.T(L.Saved.NoticeNewMatch, source, address),
            NoticeKind.EntryOpened => Loc.T(L.Saved.NoticeOpened, address),
            NoticeKind.EntryClosing => Loc.T(L.Saved.NoticeClosing, NoticeRules.ClosingLeadMinutes, address),
            _ => Loc.T(L.Saved.NoticeResults, address),
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
