using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Find.Model;

// A value nobody reported never passes a filter that narrows on it: an unknown price is not under the ceiling, an unknown
// entry count is not inside the range, an unknown size is only shown while every size is.
internal readonly record struct PlotFilter
{
    public const int FirstPlotNumber = 1;
    public const int LastPlotNumber = HousingDistricts.PlotsPerDivision * 2;

    public SizeFilter Sizes { get; init; }

    public BuyerType Buyer { get; init; }

    public long PriceCeiling { get; init; }

    public StatusFilter Statuses { get; init; }

    public int FirstPlot { get; init; }

    public int LastPlot { get; init; }

    public int? MinimumEntries { get; init; }

    public int? MaximumEntries { get; init; }

    public int MaximumAgeHours { get; init; }

    public static PlotFilter Default => new()
    {
        Sizes = SizeFilter.All,
        Buyer = BuyerType.Unknown,
        PriceCeiling = 0,
        Statuses = StatusFilter.EntryOpen,
        FirstPlot = FirstPlotNumber,
        LastPlot = LastPlotNumber,
    };

    public bool HasPlotRange => FirstPlot > FirstPlotNumber || LastPlot < LastPlotNumber;

    public bool HasEntryRange => MinimumEntries is not null || MaximumEntries is not null;

    public int MoreFiltersActive
    {
        get
        {
            var active = 0;
            if (HasPlotRange)
            {
                active++;
            }

            if (HasEntryRange)
            {
                active++;
            }

            if (MaximumAgeHours > 0)
            {
                active++;
            }

            if ((Statuses & ~(StatusFilter.EntryOpen | StatusFilter.Results | StatusFilter.NextCycle)) != 0)
            {
                active++;
            }

            return active;
        }
    }

    public bool Matches(in ResultRow row, DateTime nowUtc)
    {
        if ((Statuses & ToFilter(row.Status)) == 0)
        {
            return false;
        }

        if (Sizes != SizeFilter.All && (row.Size is not { } size || (Sizes & ToFilter(size)) == 0))
        {
            return false;
        }

        if (Buyer != BuyerType.Unknown && (row.Buyers is not { } buyers || EligibilityRules.Allows(buyers, Buyer) != true))
        {
            return false;
        }

        if (PriceCeiling > 0 && (row.Price is not { } price || price > PriceCeiling))
        {
            return false;
        }

        if (row.Key.Plot < FirstPlot || row.Key.Plot > LastPlot)
        {
            return false;
        }

        if (HasEntryRange && (row.Entries is not { } entries || entries < (MinimumEntries ?? 0) || entries > (MaximumEntries ?? int.MaxValue)))
        {
            return false;
        }

        return MaximumAgeHours <= 0 || (row.ObservedUtc != default && nowUtc - row.ObservedUtc <= TimeSpan.FromHours(MaximumAgeHours));
    }

    public static StatusFilter ToFilter(PlotStatus status) => (StatusFilter)(1 << (int)status);

    public static SizeFilter ToFilter(PlotSize size) => size switch
    {
        PlotSize.Small => SizeFilter.Small,
        PlotSize.Medium => SizeFilter.Medium,
        PlotSize.Large => SizeFilter.Large,
        _ => SizeFilter.None,
    };
}
