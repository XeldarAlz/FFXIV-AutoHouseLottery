namespace AutoHouseLottery;

public sealed partial class Configuration
{
    // The characters seen on this install, so My lottery can name the character of an entry made on another login.
    // AccountGroup stays unset until the player groups characters by service account.
    public Dictionary<ulong, KnownCharacter> Characters { get; set; } = [];

    public void Remember(ulong contentId, string name, uint homeWorldId)
    {
        if (Characters.TryGetValue(contentId, out var known) && known.Name == name && known.HomeWorldId == homeWorldId)
        {
            return;
        }

        Characters[contentId] = new KnownCharacter(name, homeWorldId, known.AccountGroup);
        SaveDebounced();
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

public readonly record struct KnownCharacter(string Name, uint HomeWorldId, int? AccountGroup);
