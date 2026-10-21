using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Game.Model;

// Keyword is what the debug command accepts; AetheryteId is the city aetheryte Lifestream names the district by
// (its ResidentialAetheryteKind values); UnlockQuestId is the Quest sheet row that opens the district to the character.
internal readonly record struct ResidentialDistrict(string Keyword, uint TerritoryTypeId, uint AetheryteId, uint UnlockQuestId);

internal static class ResidentialDistricts
{
    // The game counts wards and plots from zero; addresses shown to the player count from one.
    public const int OneBased = 1;
    public const int WardCount = 30;
    public const int PlotsPerWard = 60;
    public const int PlotsPerDivision = 30;
    public const int MainDivision = 1;
    public const int Subdivision = 2;
    public const int NotResidential = -1;

    // Unlock quests, in order: Where the Heart Is (Mist, The Lavender Beds, The Goblet), I Dream of Shirogane, Ascending to Empyreum.
    public static readonly ResidentialDistrict Mist = new("mist", 339, 8, 66750);
    public static readonly ResidentialDistrict LavenderBeds = new("lavender", 340, 2, 66748);
    public static readonly ResidentialDistrict Goblet = new("goblet", 341, 9, 66749);
    public static readonly ResidentialDistrict Shirogane = new("shirogane", 641, 111, 68167);
    public static readonly ResidentialDistrict Empyreum = new("empyreum", 979, 70, 69708);

    public static readonly ImmutableArray<ResidentialDistrict> All = [Mist, LavenderBeds, Goblet, Shirogane, Empyreum];

    public static bool TryFindByTerritory(uint territoryTypeId, out ResidentialDistrict district)
    {
        var districtIndex = IndexOfTerritory(territoryTypeId);
        district = districtIndex == NotResidential ? default : All[districtIndex];
        return districtIndex != NotResidential;
    }

    public static int IndexOfTerritory(uint territoryTypeId)
    {
        for (var districtIndex = 0; districtIndex < All.Length; districtIndex++)
        {
            if (All[districtIndex].TerritoryTypeId == territoryTypeId)
            {
                return districtIndex;
            }
        }

        return NotResidential;
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
