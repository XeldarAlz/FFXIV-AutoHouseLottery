namespace AutoHouseLottery;

public sealed partial class Configuration
{
    private const int NoticeRetentionDays = 120;

    // Automatic refunds are on for every character unless the player turned them off, so only the exceptions are kept.
    public List<ulong> ManualRefundCharacters { get; set; } = [];

    // Settlement notices already shown, by key, so each fires once, across every reload.
    public Dictionary<string, DateTime> DeliveredSettlementNotices { get; set; } = [];

    public bool AutomaticRefunds(ulong contentId) => !ManualRefundCharacters.Contains(contentId);

    public void SetAutomaticRefunds(ulong contentId, bool enabled)
    {
        var changed = enabled ? ManualRefundCharacters.Remove(contentId) : !ManualRefundCharacters.Contains(contentId);
        if (!changed)
        {
            return;
        }

        if (!enabled)
        {
            ManualRefundCharacters.Add(contentId);
        }

        Save();
    }

    public bool SettlementNoticeDelivered(string key) => DeliveredSettlementNotices.ContainsKey(key);

    public void RecordSettlementNotice(string key, DateTime nowUtc)
    {
        DeliveredSettlementNotices[key] = nowUtc;
        var retention = TimeSpan.FromDays(NoticeRetentionDays);
        var expired = new List<string>();
        foreach (var (delivered, atUtc) in DeliveredSettlementNotices)
        {
            if (nowUtc - atUtc > retention)
            {
                expired.Add(delivered);
            }
        }

        for (var index = 0; index < expired.Count; index++)
        {
            DeliveredSettlementNotices.Remove(expired[index]);
        }

        SaveDebounced();
    }

    // A hand-edited file can drop either list; both are put right once on load.
    private bool RepairSettlementSettings()
    {
        var changed = false;
        if (ManualRefundCharacters is null)
        {
            ManualRefundCharacters = [];
            changed = true;
        }

        if (DeliveredSettlementNotices is null)
        {
            DeliveredSettlementNotices = [];
            changed = true;
        }

        return changed;
    }
}
