namespace AutoHouseLottery.Core.Game.Model;

// Where the character stands in housing terms. Ward, plot and division are null where the game reports none (outside a
// residential ward, off a plot's grounds). Division 1 is the main division and 2 the subdivision.
internal readonly record struct HousingLocation(uint TerritoryTypeId, int? WardIndex, int? PlotIndex, int? Division, bool Inside)
{
    private const int NoDivision = 0;

    public static HousingLocation FromRaw(uint territoryTypeId, sbyte wardIndex, sbyte plotIndex, byte division, bool inside)
        => new(
            territoryTypeId,
            wardIndex >= 0 ? wardIndex : null,
            plotIndex >= 0 ? plotIndex : null,
            division == NoDivision ? null : division,
            inside);
}

// An estate the game lists as owned by this character or its Free Company, decoded from its house ID.
internal readonly record struct EstateAddress(uint TerritoryTypeId, ushort WorldId, int WardIndex, int PlotIndex, bool Apartment, ulong RawId);

// Gil, the rank and every Free Company and estate field are null when the game did not supply them.
internal sealed record CharacterContext(
    ulong ContentId,
    string Name,
    uint HomeWorldId,
    uint CurrentWorldId,
    uint? Gil,
    short Level,
    uint GrandCompanyId,
    byte? GrandCompanyRank,
    ulong? FreeCompanyId,
    string? FreeCompanyName,
    string? FreeCompanyTag,
    ushort? FreeCompanyHomeWorldId,
    HousingLocation Location,
    EstateAddress? PersonalEstate,
    EstateAddress? FreeCompanyEstate,
    EstateAddress? ApartmentRoom,
    DateTime ReadAtUtc)
{
    public bool OnHomeWorld => HomeWorldId == CurrentWorldId;
}
