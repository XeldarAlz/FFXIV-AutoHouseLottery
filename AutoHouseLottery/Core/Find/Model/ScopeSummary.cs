using System.Collections.Immutable;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Find.Model;

// What the Add all matching label says about its scope, such as Mist and Lavender Beds · wards 1-10 · both divisions.
internal readonly record struct ScopeSummary(
    ImmutableArray<ushort> Districts,
    bool AllDistricts,
    WardSpan Wards,
    int FirstWard,
    int LastWard,
    int WardCount,
    DivisionSpan Divisions)
{
    public static ScopeSummary Describe(in AreaScope scope)
    {
        var districts = scope.DistrictIds();
        var wardLimit = WardLimit(districts);
        var divisions = scope.MainDivision && scope.Subdivision ? DivisionSpan.Both
            : scope.MainDivision ? DivisionSpan.Main
            : scope.Subdivision ? DivisionSpan.Subdivision
            : DivisionSpan.None;
        var allDistricts = districts.Length == HousingDistricts.All.Length;
        if (scope.AllWards)
        {
            return new ScopeSummary(districts, allDistricts, WardSpan.All, 1, wardLimit, wardLimit, divisions);
        }

        var first = 0;
        var last = 0;
        var count = 0;
        for (var ward = 1; ward <= wardLimit; ward++)
        {
            if (!scope.IncludesWard(ward))
            {
                continue;
            }

            first = first == 0 ? ward : first;
            last = ward;
            count++;
        }

        var span = count == 0 ? WardSpan.None
            : count == wardLimit ? WardSpan.All
            : count == 1 ? WardSpan.Single
            : count == last - first + 1 ? WardSpan.Range
            : WardSpan.Several;
        return new ScopeSummary(districts, allDistricts, span, first, last, count, divisions);
    }

    private static int WardLimit(ImmutableArray<ushort> districts)
    {
        var limit = 0;
        for (var index = 0; index < districts.Length; index++)
        {
            if (HousingDistricts.TryGet(districts[index], out var district) && district.WardCount > limit)
            {
                limit = district.WardCount;
            }
        }

        return limit;
    }
}
