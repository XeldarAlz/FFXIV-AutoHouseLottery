namespace AutoHouseLottery;

public sealed partial class Configuration
{
    // Find a home's character roster: which known characters a batch enters for, and each one's own buyer type, target,
    // deposit cap and reserve. A character without a choice here is not selected.
    public Dictionary<ulong, RosterChoice> Roster { get; set; } = [];

    // How a batch places its entries, as a PlacementChoice number: 0 Same plot inside an ownership group and Spread across
    // groups, 1 Same plot for everyone, 2 Spread.
    public byte BatchPlacement { get; set; }

    // Switch characters to collect refunds: off by default, and even when on it acts only once switching is allowed.
    public bool SwitchForRefunds { get; set; }

    public RosterChoice ChoiceFor(ulong contentId) => Roster.TryGetValue(contentId, out var choice) ? choice : default;

    public void SetChoice(ulong contentId, RosterChoice choice)
    {
        if (ChoiceFor(contentId) == choice)
        {
            return;
        }

        Roster[contentId] = choice;
        RosterRevision++;
        SaveDebounced();
    }

    public void SetBatchPlacement(byte placement)
    {
        if (BatchPlacement == placement)
        {
            return;
        }

        BatchPlacement = placement;
        RosterRevision++;
        Save();
    }

    public void SetSwitchForRefunds(bool enabled)
    {
        if (SwitchForRefunds == enabled)
        {
            return;
        }

        SwitchForRefunds = enabled;
        Save();
    }

    // A hand-edited file can drop the roster, or keep choices for characters it no longer knows; both are put right once on load.
    private bool RepairRoster()
    {
        if (Roster is null)
        {
            Roster = [];
            return true;
        }

        var unknown = new List<ulong>();
        foreach (var contentId in Roster.Keys)
        {
            if (!Characters.ContainsKey(contentId))
            {
                unknown.Add(contentId);
            }
        }

        for (var index = 0; index < unknown.Count; index++)
        {
            Roster.Remove(unknown[index]);
        }

        return unknown.Count > 0;
    }
}

// One character's batch settings, as plain numbers so the settings file never depends on an internal type. Buyer 0 and a
// DepositCap of 0 take the Find a home filters' buyer and price ceiling; a null Reserve takes the shared reserve; a
// TargetDistrict of 0 takes the best match instead of one plot on the character's home world.
public readonly record struct RosterChoice(bool Selected, byte Buyer, long DepositCap, long? Reserve, uint TargetDistrict, int TargetWard, int TargetPlot);
