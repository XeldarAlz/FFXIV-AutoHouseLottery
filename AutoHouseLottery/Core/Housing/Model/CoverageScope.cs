namespace AutoHouseLottery.Core.Housing.Model;

internal readonly record struct CoverageScope
{
    private const int MaximumMaskedWards = 64;

    private CoverageScope(GameRegion region, uint worldId, ushort districtId, ulong wardMask)
    {
        Region = region;
        WorldId = worldId;
        DistrictId = districtId;
        WardMask = wardMask;
    }

    public GameRegion Region { get; }

    public uint WorldId { get; }

    public ushort DistrictId { get; }

    public ulong WardMask { get; }

    public static bool TryForDistrict(GameRegion region, uint worldId, uint districtId, out CoverageScope scope)
    {
        scope = default;
        if (!TryResolve(region, worldId, districtId, out var district))
        {
            return false;
        }

        var allWards = district.WardCount == MaximumMaskedWards ? ulong.MaxValue : (1UL << district.WardCount) - 1;
        scope = new CoverageScope(region, worldId, district.TerritoryId, allWards);
        return true;
    }

    public static bool TryForWard(GameRegion region, uint worldId, uint districtId, int ward, out CoverageScope scope)
    {
        scope = default;
        if (!TryResolve(region, worldId, districtId, out var district) || ward < 1 || ward > district.WardCount)
        {
            return false;
        }

        scope = new CoverageScope(region, worldId, district.TerritoryId, WardBit(ward));
        return true;
    }

    public bool Contains(in PlotKey key)
    {
        return key.Region == Region
            && key.WorldId == WorldId
            && key.DistrictId == DistrictId
            && (WardMask & WardBit(key.Ward)) != 0;
    }

    private static bool TryResolve(GameRegion region, uint worldId, uint districtId, out HousingDistrict district)
    {
        district = default;
        return region != GameRegion.Unknown
            && worldId != 0
            && HousingDistricts.TryGet(districtId, out district)
            && district.WardCount <= MaximumMaskedWards;
    }

    private static ulong WardBit(int ward) => 1UL << (ward - 1);
}
