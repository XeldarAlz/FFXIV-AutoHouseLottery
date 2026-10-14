using AutoHouseLottery.Core.Find.Model;

namespace AutoHouseLottery;

public sealed partial class Configuration
{
    public FindSettings Find { get; set; } = new();

    public bool NotifySaved { get; set; } = true;

    // A hand-edited file can drop the Find a home settings or their lists; both are put right once on load.
    private bool RepairFindSettings()
    {
        if (Find is null)
        {
            Find = new FindSettings();
            return true;
        }

        var changed = false;
        if (Find.Wards is null)
        {
            Find.Wards = [];
            changed = true;
        }

        if (Find.PreferredDistricts is null)
        {
            Find.PreferredDistricts = [];
            changed = true;
        }

        return changed;
    }
}

// Stored as plain numbers so the settings file never depends on the shape of an internal type. Null districts mean every
// district, and world 0 means the current character's home world.
[Serializable]
public sealed class FindSettings
{
    public uint WorldId { get; set; }

    public List<uint>? Districts { get; set; }

    public bool AllWards { get; set; } = true;

    public List<int> Wards { get; set; } = [];

    public bool MainDivision { get; set; } = true;

    public bool Subdivision { get; set; } = true;

    public byte Sizes { get; set; } = (byte)SizeFilter.All;

    public byte Buyer { get; set; }

    public long PriceCeiling { get; set; }

    public byte Statuses { get; set; } = (byte)StatusFilter.EntryOpen;

    public int FirstPlot { get; set; } = PlotFilter.FirstPlotNumber;

    public int LastPlot { get; set; } = PlotFilter.LastPlotNumber;

    public int? MinimumEntries { get; set; }

    public int? MaximumEntries { get; set; }

    public int MaximumAgeHours { get; set; }

    public byte Sort { get; set; }

    public bool ListView { get; set; }

    public List<uint> PreferredDistricts { get; set; } = [];

    public long GilReserve { get; set; }
}
