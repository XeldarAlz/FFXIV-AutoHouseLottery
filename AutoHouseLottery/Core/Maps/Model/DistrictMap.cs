namespace AutoHouseLottery.Core.Maps.Model;

internal sealed record DistrictMap(ushort DistrictId, DivisionMap? Main, DivisionMap? Subdivision, string? ArtPath, MapFailure Failure, string Detail)
{
    public DivisionMap? For(bool subdivision) => subdivision ? Subdivision : Main;
}
