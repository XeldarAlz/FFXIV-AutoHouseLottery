using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Core.Maps;

namespace AutoHouseLottery.Core.Find;

// Plot addresses in the client's district names and the plugin's language. A list row asks for its address on every
// frame, so each one is built once and kept until the language changes; the cache holds at most one entry per known plot.
internal static class PlotLabels
{
    private static readonly Dictionary<PlotKey, string> addresses = [];
    private static readonly Dictionary<PlotKey, string> addressesOnWorld = [];
    private static LanguageInfo? language;
    private static bool namesReady;

    public static string Address(GameNames names, PlotKey key) => Cached(addresses, names, key, false);

    public static string AddressOnWorld(GameNames names, PlotKey key) => Cached(addressesOnWorld, names, key, true);

    private static string Cached(Dictionary<PlotKey, string> cache, GameNames names, PlotKey key, bool withWorld)
    {
        if (!ReferenceEquals(language, Loc.Current) || namesReady != names.Ready)
        {
            addresses.Clear();
            addressesOnWorld.Clear();
            language = Loc.Current;
            namesReady = names.Ready;
        }

        if (cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var district = names.District(key.DistrictId);
        var address = withWorld
            ? Loc.T(L.Find.AddressOnWorld, district, key.Ward, key.Plot, names.World(key.WorldId))
            : Loc.T(L.Find.Address, district, key.Ward, key.Plot);
        cache[key] = address;
        return address;
    }
}
