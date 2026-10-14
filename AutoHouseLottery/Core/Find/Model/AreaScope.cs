using System.Collections.Immutable;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Find.Model;

// Districts are held as a bit per entry of HousingDistricts.All and wards as a bit per ward, so a scope is a small value
// that compares by value. All wards stays a flag of its own, so a district whose metadata gains wards keeps covering them.
internal readonly record struct AreaScope(uint WorldId, uint DistrictMask, bool AllWards, ulong WardMask, bool MainDivision, bool Subdivision)
{
    public const int MaximumWards = 64;

    public static uint AllDistrictsMask => (1u << HousingDistricts.All.Length) - 1;

    public bool HasDistricts => (DistrictMask & AllDistrictsMask) != 0;

    public bool HasWards => AllWards || WardMask != 0;

    public bool HasDivisions => MainDivision || Subdivision;

    public bool SelectsNothing => !HasDistricts || !HasWards || !HasDivisions;

    public bool IsEmpty => WorldId == 0 || SelectsNothing;

    public static uint DistrictBit(uint districtId)
    {
        for (var index = 0; index < HousingDistricts.All.Length; index++)
        {
            if (HousingDistricts.All[index].TerritoryId == districtId)
            {
                return 1u << index;
            }
        }

        return 0;
    }

    public static ulong WardBit(int ward) => ward is >= 1 and <= MaximumWards ? 1UL << (ward - 1) : 0;

    public static ulong WardRange(int firstWard, int lastWard)
    {
        var mask = 0UL;
        for (var ward = Math.Max(1, firstWard); ward <= Math.Min(MaximumWards, lastWard); ward++)
        {
            mask |= WardBit(ward);
        }

        return mask;
    }

    public static uint DistrictMaskOf(ReadOnlySpan<uint> districtIds)
    {
        var mask = 0u;
        for (var index = 0; index < districtIds.Length; index++)
        {
            mask |= DistrictBit(districtIds[index]);
        }

        return mask;
    }

    public static ulong WardMaskOf(ReadOnlySpan<int> wards)
    {
        var mask = 0UL;
        for (var index = 0; index < wards.Length; index++)
        {
            mask |= WardBit(wards[index]);
        }

        return mask;
    }

    public int[] WardNumbers()
    {
        var wards = new List<int>();
        for (var ward = 1; ward <= MaximumWards; ward++)
        {
            if ((WardMask & WardBit(ward)) != 0)
            {
                wards.Add(ward);
            }
        }

        return [.. wards];
    }

    public ImmutableArray<ushort> DistrictIds()
    {
        var districts = ImmutableArray.CreateBuilder<ushort>(HousingDistricts.All.Length);
        for (var index = 0; index < HousingDistricts.All.Length; index++)
        {
            if ((DistrictMask & (1u << index)) != 0)
            {
                districts.Add(HousingDistricts.All[index].TerritoryId);
            }
        }

        return districts.ToImmutable();
    }

    public bool IncludesDistrict(uint districtId) => (DistrictMask & DistrictBit(districtId)) != 0;

    public bool IncludesWard(int ward) => AllWards || (WardMask & WardBit(ward)) != 0;

    public bool IncludesDivision(bool subdivision) => subdivision ? Subdivision : MainDivision;

    public bool Contains(in PlotKey key)
    {
        return key.WorldId == WorldId
            && IncludesDistrict(key.DistrictId)
            && IncludesWard(key.Ward)
            && IncludesDivision(key.IsSubdivision);
    }
}
