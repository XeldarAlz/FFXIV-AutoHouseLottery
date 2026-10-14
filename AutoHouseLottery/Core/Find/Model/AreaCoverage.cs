using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Find.Model;

internal static class AreaCoverage
{
    // An area is covered when the listings have answered for at least one of its districts, even if the answer was that
    // the district has no open plots; only then does an empty result mean no plot matches.
    public static bool Covered(IReadOnlyDictionary<(uint World, ushort District), CoverageSnapshot> coverage, in AreaScope area)
    {
        for (var index = 0; index < HousingDistricts.All.Length; index++)
        {
            var districtId = HousingDistricts.All[index].TerritoryId;
            if (area.IncludesDistrict(districtId)
                && coverage.TryGetValue((area.WorldId, districtId), out var snapshot)
                && snapshot.Outcome != CoverageOutcome.Failed)
            {
                return true;
            }
        }

        return false;
    }
}
