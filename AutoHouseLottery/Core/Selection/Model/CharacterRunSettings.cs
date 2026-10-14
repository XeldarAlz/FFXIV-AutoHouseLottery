using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Selection.Model;

internal readonly record struct CharacterRunSettings(ulong ContentId, BuyerType Buyer, long DepositCap, long GilReserve);
