using System.Globalization;

namespace AutoHouseLottery.Core.Housing.Model;

internal readonly record struct PlotKey : IComparable<PlotKey>
{
    private PlotKey(GameRegion region, uint worldId, ushort districtId, byte ward, byte plot)
    {
        Region = region;
        WorldId = worldId;
        DistrictId = districtId;
        Ward = ward;
        Plot = plot;
    }

    public GameRegion Region { get; }

    public uint WorldId { get; }

    public ushort DistrictId { get; }

    public byte Ward { get; }

    public byte Plot { get; }

    public bool IsValid => Region != GameRegion.Unknown;

    public bool IsSubdivision => Plot > HousingDistricts.PlotsPerDivision;

    public static bool TryCreate(GameRegion region, uint worldId, uint districtId, int ward, int plot, out PlotKey key)
    {
        key = default;
        if (region == GameRegion.Unknown || worldId == 0)
        {
            return false;
        }

        if (!HousingDistricts.TryGet(districtId, out var district))
        {
            return false;
        }

        if (ward < 1 || ward > district.WardCount || plot < 1 || plot > district.PlotsPerWard)
        {
            return false;
        }

        key = new PlotKey(region, worldId, district.TerritoryId, (byte)ward, (byte)plot);
        return true;
    }

    public int CompareTo(PlotKey other)
    {
        var comparison = Region.CompareTo(other.Region);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = WorldId.CompareTo(other.WorldId);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = DistrictId.CompareTo(other.DistrictId);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = Ward.CompareTo(other.Ward);
        return comparison != 0 ? comparison : Plot.CompareTo(other.Plot);
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Region}:{WorldId}:{DistrictId}:{Ward}:{Plot}");
}
