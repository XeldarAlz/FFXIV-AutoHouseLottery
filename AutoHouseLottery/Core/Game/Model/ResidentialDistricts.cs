using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Game.Model;

// Keyword is what the debug command accepts; AetheryteId is the city aetheryte Lifestream names the district by
// (its ResidentialAetheryteKind values).
internal readonly record struct ResidentialDistrict(string Keyword, uint TerritoryTypeId, uint AetheryteId);

internal static class ResidentialDistricts
{
    // The game counts wards and plots from zero; addresses shown to the player count from one.
    public const int OneBased = 1;
    public const int WardCount = 30;
    public const int PlotsPerWard = 60;
    public const int PlotsPerDivision = 30;
    public const int MainDivision = 1;
    public const int Subdivision = 2;

    public static readonly ResidentialDistrict Mist = new("mist", 339, 8);
    public static readonly ResidentialDistrict LavenderBeds = new("lavender", 340, 2);
    public static readonly ResidentialDistrict Goblet = new("goblet", 341, 9);
    public static readonly ResidentialDistrict Shirogane = new("shirogane", 641, 111);
    public static readonly ResidentialDistrict Empyreum = new("empyreum", 979, 70);

    public static readonly ImmutableArray<ResidentialDistrict> All = [Mist, LavenderBeds, Goblet, Shirogane, Empyreum];

    public static bool TryFindByTerritory(uint territoryTypeId, out ResidentialDistrict district)
    {
        for (var districtIndex = 0; districtIndex < All.Length; districtIndex++)
        {
            if (All[districtIndex].TerritoryTypeId == territoryTypeId)
            {
                district = All[districtIndex];
                return true;
            }
        }

        district = default;
        return false;
    }

    public static bool TryFindByKeyword(string keyword, out ResidentialDistrict district)
    {
        for (var districtIndex = 0; districtIndex < All.Length; districtIndex++)
        {
            if (string.Equals(All[districtIndex].Keyword, keyword, StringComparison.OrdinalIgnoreCase))
            {
                district = All[districtIndex];
                return true;
            }
        }

        district = default;
        return false;
    }

    public static bool IsResidential(uint territoryTypeId) => TryFindByTerritory(territoryTypeId, out _);

    public static int DivisionOfPlot(int plotIndex) => plotIndex < PlotsPerDivision ? MainDivision : Subdivision;

    public static bool IsWardIndex(int wardIndex) => wardIndex is >= 0 and < WardCount;

    public static bool IsPlotIndex(int plotIndex) => plotIndex is >= 0 and < PlotsPerWard;
}
