using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Eligibility.Model;

internal readonly record struct OwnershipGroup(OwnershipGroupKind Kind, GameRegion Region, uint HomeWorldId, ulong Id);
