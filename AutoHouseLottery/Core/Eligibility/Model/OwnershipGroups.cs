using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Eligibility.Model;

internal static class OwnershipGroups
{
    private const ulong UngroupedId = 0;

    public static OwnershipGroup For(in CharacterProfile character, BuyerType buyer)
    {
        if (buyer == BuyerType.FreeCompany)
        {
            return new OwnershipGroup(OwnershipGroupKind.FreeCompany, character.Region, character.HomeWorldId, character.FreeCompanyId ?? CharacterProfile.NoFreeCompany);
        }

        if (character.AccountGroup is { } accountGroup)
        {
            return new OwnershipGroup(OwnershipGroupKind.ServiceAccount, character.Region, character.HomeWorldId, (ulong)accountGroup);
        }

        return new OwnershipGroup(OwnershipGroupKind.UngroupedHomeWorld, character.Region, character.HomeWorldId, UngroupedId);
    }
}
