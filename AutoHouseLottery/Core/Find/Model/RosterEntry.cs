using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Integration.Model;

namespace AutoHouseLottery.Core.Find.Model;

// A character's own batch settings. Unknown Buyer, a DepositCap of 0 and a null Reserve take the filters' buyer, the price
// ceiling and the shared reserve; a null Target takes the best match.
internal readonly record struct RosterSelection(bool Selected, BuyerType Buyer, long DepositCap, long? Reserve, PlotKey? Target);

// One known character on the Characters expander: what its last login showed and what the player chose for it.
internal readonly record struct RosterEntry(RosterFacts Facts, RosterSelection Selection)
{
    public ulong ContentId => Facts.ContentId;
}
