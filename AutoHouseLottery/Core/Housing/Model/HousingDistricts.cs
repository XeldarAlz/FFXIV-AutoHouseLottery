using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Housing.Model;

internal static class HousingDistricts
{
    public const int PlotsPerDivision = 30;

    // A fully devalued small plot, the cheapest price the listings show; no plot is offered for less.
    public const long MinimumPlotPrice = 3_000_000;

    public const ushort Mist = 339;
    public const ushort LavenderBeds = 340;
    public const ushort Goblet = 341;
    public const ushort Shirogane = 641;
    public const ushort Empyreum = 979;

    private const byte StandardWardCount = 30;
    private const byte StandardPlotsPerWard = PlotsPerDivision * 2;

    public static readonly ImmutableArray<HousingDistrict> All =
    [
        new(Mist, StandardWardCount, StandardPlotsPerWard),
        new(LavenderBeds, StandardWardCount, StandardPlotsPerWard),
        new(Goblet, StandardWardCount, StandardPlotsPerWard),
        new(Shirogane, StandardWardCount, StandardPlotsPerWard),
        new(Empyreum, StandardWardCount, StandardPlotsPerWard),
    ];

    public static bool TryGet(uint territoryId, out HousingDistrict district)
    {
        for (var index = 0; index < All.Length; index++)
        {
            if (All[index].TerritoryId == territoryId)
            {
                district = All[index];
                return true;
            }
        }

        district = default;
        return false;
    }
}
