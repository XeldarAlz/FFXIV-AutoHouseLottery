using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Integration.Model;

internal static class ReadingAddress
{

    // Whether the lottery reading names this plot: the record's address is compared field by field; the Timers text is
    // compared the way the placard's text is, by the district's name and the ward and plot numbers. Null when the
    // reading carries no address that can be compared.
    public static bool? Names(in LotteryEntry entry, PlotKey plot, Func<uint, string?> districtNameOf)
    {
        if (entry.TerritoryTypeId is { } territory && entry.WardIndex is { } ward && entry.PlotIndex is { } plotIndex)
        {
            return territory == plot.DistrictId && ward + ResidentialDistricts.OneBased == plot.Ward && plotIndex + ResidentialDistricts.OneBased == plot.Plot;
        }

        if (entry.AddressText is not { } text || districtNameOf(plot.DistrictId) is not { } districtName)
        {
            return null;
        }

        var address = new PlotAddress(plot.DistrictId, plot.Ward - ResidentialDistricts.OneBased, plot.Plot - ResidentialDistricts.OneBased);
        return PlacardAddressRule.TextNames(text, address, districtName);
    }
}
