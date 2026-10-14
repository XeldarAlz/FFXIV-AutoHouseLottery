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
        Save();
    }

    private bool RepairCharacters()
    {
        if (Characters is not null)
        {
            return false;
        }

        Characters = [];
        return true;
    }
}

public readonly record struct KnownCharacter(string Name, uint HomeWorldId, int? AccountGroup);
