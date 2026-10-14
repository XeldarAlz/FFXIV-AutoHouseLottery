namespace AutoHouseLottery.Core.Housing.Model;

// A world's region, from its data center's row in the game's WorldDCGroupType sheet. The housing service, the lottery
// calendar and the dialog rows this plugin reads are the global client's, which serves the Japanese, North American,
// European and Oceanian worlds. Chinese and Korean worlds run their own clients and services, so housing on them is
// refused rather than guessed at.
internal enum WorldRegion : byte
{
    Unknown,
    Japan,
    NorthAmerica,
    Europe,
    Oceania,
    China,
    Korea,
}

internal static class WorldRegions
{
    private const uint JapanRow = 1;
    private const uint NorthAmericaRow = 2;
    private const uint EuropeRow = 3;
    private const uint OceaniaRow = 4;
    private const uint ChinaRow = 5;
    private const uint KoreaRow = 6;

    public static WorldRegion FromDataCenterRegion(uint regionRow) => regionRow switch
    {
        JapanRow => WorldRegion.Japan,
        NorthAmericaRow => WorldRegion.NorthAmerica,
        EuropeRow => WorldRegion.Europe,
        OceaniaRow => WorldRegion.Oceania,
        ChinaRow => WorldRegion.China,
        KoreaRow => WorldRegion.Korea,
        _ => WorldRegion.Unknown,
    };

    // A world whose region could not be read is refused too: nothing about it can be checked against the global data.
    public static bool Supported(WorldRegion region) => region is WorldRegion.Japan or WorldRegion.NorthAmerica or WorldRegion.Europe or WorldRegion.Oceania;
}
