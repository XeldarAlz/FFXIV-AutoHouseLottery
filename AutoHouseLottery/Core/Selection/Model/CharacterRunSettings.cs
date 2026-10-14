using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Selection.Model;

// Target names one plot of the shortlist this character enters exactly, with no fallback; null takes the best match.
// ManualRefunds: the character's automatic refunds are off, so an earlier refund still due rules it out instead of being
// collected first.
internal readonly record struct CharacterRunSettings(ulong ContentId, BuyerType Buyer, long DepositCap, long GilReserve)
{
    public PlotKey? Target { get; init; }

    public bool ManualRefunds { get; init; }
}
