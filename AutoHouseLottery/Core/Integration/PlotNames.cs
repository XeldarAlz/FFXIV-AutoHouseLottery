using AutoHouseLottery.Core.Game;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Localization;

namespace AutoHouseLottery.Core.Integration;

internal static class PlotNames
{
    // "Mist · Ward 12 · Plot 5", with the district named in the client's language.
    public static string Address(PlotKey plot)
        => Loc.T(L.Run.Address, DistrictNames.Of(plot.DistrictId) ?? plot.DistrictId.ToString(Loc.Culture), plot.Ward, plot.Plot);
}
