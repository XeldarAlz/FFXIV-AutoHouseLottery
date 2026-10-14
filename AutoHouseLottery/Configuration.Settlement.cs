using Newtonsoft.Json;

namespace AutoHouseLottery;

public sealed partial class Configuration
{
    private const int NoticeRetentionDays = 120;

    // Automatic refunds are on for every character unless the player turned them off, so only the exceptions are kept.
    public List<ulong> ManualRefundCharacters { get; set; } = [];

    // Settlement notices already shown, by key, so each fires once, across every reload.
    public Dictionary<string, DateTime> DeliveredSettlementNotices { get; set; } = [];

    // Visits that found the placard offering no result, by receipt or attempt: the session they were in and whether the
    // placard was read in full. Two strong ones in different sessions are part of the absence rule; any one marks the
    // entry as still pending on My lottery.
    public Dictionary<Guid, AbsenceSighting> AbsenceSightings { get; set; } = [];

    // Changes with every sighting recorded or forgotten, so a window can tell the sightings changed without comparing them.
    [JsonIgnore]
    public int AbsenceSightingsRevision { get; private set; }

    public bool AutomaticRefunds(ulong contentId) => !ManualRefundCharacters.Contains(contentId);

    public AbsenceSighting? AbsenceSighting(Guid id) => AbsenceSightings.TryGetValue(id, out var sighting) ? sighting : null;

    // A strong sighting is kept over a weaker later one, so an unread placard never erases the evidence of a read one.
    public void RecordAbsenceSighting(Guid id, Guid session, bool strong, DateTime nowUtc)
    {
        if (AbsenceSightings.TryGetValue(id, out var earlier) && earlier.Strong && !strong)
        {
            return;
        }

        AbsenceSightings[id] = new AbsenceSighting(session, nowUtc, strong);
        AbsenceSightingsRevision++;
        var retention = TimeSpan.FromDays(NoticeRetentionDays);
        var expired = new List<Guid>();
        foreach (var (seen, sighting) in AbsenceSightings)
        {
            if (nowUtc - sighting.AtUtc > retention)
            {
                expired.Add(seen);
            }
        }

        for (var index = 0; index < expired.Count; index++)
        {
            AbsenceSightings.Remove(expired[index]);
        }

        SaveDebounced();
    }

    public void ForgetAbsenceSighting(Guid id)
    {
        if (AbsenceSightings.Remove(id))
        {
            AbsenceSightingsRevision++;
            SaveDebounced();
        }
    }

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

        RosterRevision++;
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

    // A hand-edited file can drop any of these; each is put right once on load.
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

        if (AbsenceSightings is null)
        {
            AbsenceSightings = [];
            changed = true;
        }

        return changed;
    }
}

public readonly record struct AbsenceSighting(Guid Session, DateTime AtUtc, bool Strong);
