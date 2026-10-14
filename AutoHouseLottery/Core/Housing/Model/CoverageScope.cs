namespace AutoHouseLottery.Core.Housing.Model;

internal readonly record struct CoverageScope
{
    private const int MaskedWardLimit = 64;

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
        if (region == GameRegion.Unknown || worldId == 0 || !HousingDistricts.TryGet(districtId, out var district) || district.WardCount >= MaskedWardLimit)
        {
            return false;
        }

        scope = new CoverageScope(region, worldId, district.TerritoryId, (1UL << district.WardCount) - 1);
        return true;
    }

    public bool Contains(in PlotKey key)
    {
        return key.Region == Region
            && key.WorldId == WorldId
            && key.DistrictId == DistrictId
            && (WardMask & (1UL << (key.Ward - 1))) != 0;
    }
}
