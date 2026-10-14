namespace AutoHouseLottery.Core.Housing.Model;

internal readonly struct PlotKeyJson
{
    public GameRegion? Region { get; init; }

    public uint? World { get; init; }

    public uint? District { get; init; }

    public int? Ward { get; init; }

    public int? Plot { get; init; }

    public static PlotKeyJson From(in PlotKey key) => new()
    {
        Region = key.Region,
        World = key.WorldId,
        District = key.DistrictId,
        Ward = key.Ward,
        Plot = key.Plot,
    };

    public bool TryToKey(out PlotKey key)
    {
        key = default;
        return Region is { } region
            && World is { } world
            && District is { } district
            && Ward is { } ward
            && Plot is { } plot
            && PlotKey.TryCreate(region, world, district, ward, plot, out key);
    }
}
