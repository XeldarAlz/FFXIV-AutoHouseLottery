using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Eligibility.Model;

internal static class OwnershipGroups
{
    private const ulong MergedId = 0;

    public static OwnershipGroup[] Assign(ReadOnlySpan<CharacterProfile> characters, ReadOnlySpan<BuyerType> buyers)
    {
        if (characters.Length != buyers.Length)
        {
            throw new ArgumentException("Every character needs exactly one buyer type.", nameof(buyers));
        }

        var groups = new OwnershipGroup[characters.Length];
        for (var index = 0; index < characters.Length; index++)
        {
            groups[index] = GroupOf(characters, buyers, index);
        }

        return groups;
    }

    private static OwnershipGroup GroupOf(ReadOnlySpan<CharacterProfile> characters, ReadOnlySpan<BuyerType> buyers, int index)
    {
        var character = characters[index];
        var freeCompany = buyers[index] == BuyerType.FreeCompany;
        var uncertain = HasUncertainMember(characters, buyers, character.Region, character.HomeWorldId, freeCompany);
        if (freeCompany)
        {
            return uncertain
                ? new OwnershipGroup(OwnershipGroupKind.FreeCompaniesOnHomeWorld, character.Region, character.HomeWorldId, MergedId)
                : new OwnershipGroup(OwnershipGroupKind.FreeCompany, character.Region, character.HomeWorldId, character.FreeCompanyId.GetValueOrDefault());
        }

        return uncertain
            ? new OwnershipGroup(OwnershipGroupKind.UngroupedHomeWorld, character.Region, character.HomeWorldId, MergedId)
            : new OwnershipGroup(OwnershipGroupKind.ServiceAccount, character.Region, character.HomeWorldId, (ulong)character.AccountGroup.GetValueOrDefault());
    }

    private static bool HasUncertainMember(ReadOnlySpan<CharacterProfile> characters, ReadOnlySpan<BuyerType> buyers, GameRegion region, uint homeWorldId, bool freeCompany)
    {
        for (var index = 0; index < characters.Length; index++)
        {
            var character = characters[index];
            if (character.Region != region || character.HomeWorldId != homeWorldId || (buyers[index] == BuyerType.FreeCompany) != freeCompany)
            {
                continue;
            }

            if (freeCompany ? character.FreeCompanyId is null : character.AccountGroup is null)
            {
                return true;
            }
        }

        return false;
    }
}
