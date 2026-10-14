namespace AutoHouseLottery;

public sealed partial class Configuration
{
    private static readonly TimeSpan GilRestampInterval = TimeSpan.FromHours(1);

    // The characters seen on this install, so My lottery can name the character of an entry made on another login and a
    // batch can plan a character that is not logged in. AccountGroup stays unset until the player groups characters by
    // service account; the other facts are what the character's last login showed, and its gil is only an estimate.
    public Dictionary<ulong, KnownCharacter> Characters { get; set; } = [];

    // Bumped by every change to a character, its grouping facts or the roster, so the Find a home view rebuilds its plan.
    [Newtonsoft.Json.JsonIgnore]
    public int RosterRevision { get; private set; }

    public void Remember(ulong contentId, string name, uint homeWorldId)
    {
        if (Characters.TryGetValue(contentId, out var known) && known.Name == name && known.HomeWorldId == homeWorldId)
        {
            return;
        }

        Characters[contentId] = known.Name is null ? new KnownCharacter(name, homeWorldId, null) : known with { Name = name, HomeWorldId = homeWorldId };
        RosterRevision++;
        SaveDebounced();
    }

    // What the logged-in character's profile shows now. Only a change is saved; the gil's time is renewed when the amount
    // moves, or once an hour while it stays, so its age on the roster says how old the estimate is. The logged-in character
    // is planned from its live profile, so only a change of company or land makes the roster plan again.
    public void RememberFacts(ulong contentId, long? gil, ulong? freeCompanyId, bool? ownsLand, DateTime nowUtc)
    {
        if (!Characters.TryGetValue(contentId, out var known))
        {
            return;
        }

        var stale = known.GilSeenUtc is not { } seen || nowUtc - seen >= GilRestampInterval;
        var gilSeen = gil is { } balance && (balance != known.Gil || stale);
        var updated = known with
        {
            Gil = gil ?? known.Gil,
            GilSeenUtc = gilSeen ? nowUtc : known.GilSeenUtc,
            FreeCompanyId = freeCompanyId,
            OwnsLand = ownsLand,
        };
        if (updated == known)
        {
            return;
        }

        Characters[contentId] = updated with { SeenUtc = nowUtc };
        if (updated.FreeCompanyId != known.FreeCompanyId || updated.OwnsLand != known.OwnsLand)
        {
            RosterRevision++;
        }

        SaveDebounced();
    }

    public void SetAccountGroup(ulong contentId, int? accountGroup)
    {
        if (!Characters.TryGetValue(contentId, out var known) || known.AccountGroup == accountGroup)
        {
            return;
        }

        Characters[contentId] = known with { AccountGroup = accountGroup };
        RosterRevision++;
        Save();
    }

    // A hand-edited file can carry a null roster or a character without a name; both are put right once on load.
    private bool RepairCharacters()
    {
        if (Characters is null)
        {
            Characters = [];
            return true;
        }

        var unnamed = new List<ulong>();
        foreach (var (contentId, known) in Characters)
        {
            if (string.IsNullOrEmpty(known.Name))
            {
                unnamed.Add(contentId);
            }
        }

        for (var index = 0; index < unnamed.Count; index++)
        {
            Characters.Remove(unnamed[index]);
        }

        return unnamed.Count > 0;
    }
}

public readonly record struct KnownCharacter(string Name, uint HomeWorldId, int? AccountGroup)
{
    public long? Gil { get; init; }

    public DateTime? GilSeenUtc { get; init; }

    public ulong? FreeCompanyId { get; init; }

    public bool? OwnsLand { get; init; }

    public DateTime? SeenUtc { get; init; }
}
