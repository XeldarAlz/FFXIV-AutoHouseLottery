using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Selection.Model;

// Target names one plot of the shortlist this character enters exactly, with no fallback; null takes the best match.
internal readonly record struct CharacterRunSettings(ulong ContentId, BuyerType Buyer, long DepositCap, long GilReserve)
{
    public PlotKey? Target { get; init; }
}
