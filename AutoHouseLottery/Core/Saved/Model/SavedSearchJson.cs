using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Saved.Model;

internal sealed class SavedSearchJson
{
    public Guid? Id { get; init; }

    public string? Name { get; init; }

    public DateTime? CreatedUtc { get; init; }

    public uint? World { get; init; }

    public uint[]? Districts { get; init; }

    public bool? AllWards { get; init; }

    public int[]? Wards { get; init; }

    public bool? MainDivision { get; init; }

    public bool? Subdivision { get; init; }

    public SizeFilter? Sizes { get; init; }

    public BuyerType? Buyer { get; init; }

    public long? PriceCeiling { get; init; }

    public StatusFilter? Statuses { get; init; }

    public int? FirstPlot { get; init; }

    public int? LastPlot { get; init; }

    public int? MinimumEntries { get; init; }

    public int? MaximumEntries { get; init; }

    public int? MaximumAgeHours { get; init; }
}
