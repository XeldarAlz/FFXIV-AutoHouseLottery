namespace AutoHouseLottery.Core.Saved.Model;

// What has already been announced, kept across reloads so a notice fires once even when the plugin catches up on login.
internal sealed class NoticeLedger
{
    public const int RetentionDays = 30;

    private static readonly TimeSpan retention = TimeSpan.FromDays(RetentionDays);

    private readonly Dictionary<string, DateTime> delivered = new(StringComparer.Ordinal);

    public int Count => delivered.Count;

    public IReadOnlyDictionary<string, DateTime> Entries => delivered;

    public bool Contains(in Notice notice) => delivered.ContainsKey(notice.DedupeKey);

    public bool TryRecord(in Notice notice, DateTime nowUtc) => delivered.TryAdd(notice.DedupeKey, nowUtc);

    public void Restore(string key, DateTime recordedUtc) => delivered[key] = recordedUtc;

    public NoticeLedger Copy()
    {
        var copy = new NoticeLedger();
        foreach (var entry in delivered)
        {
            copy.delivered[entry.Key] = entry.Value;
        }

        return copy;
    }

    public int Prune(DateTime nowUtc)
    {
        var expired = new List<string>();
        foreach (var entry in delivered)
        {
            if (nowUtc - entry.Value > retention)
            {
                expired.Add(entry.Key);
            }
        }

        for (var index = 0; index < expired.Count; index++)
        {
            delivered.Remove(expired[index]);
        }

        return expired.Count;
    }
}
