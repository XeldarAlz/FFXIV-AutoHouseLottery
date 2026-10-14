using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Housing.Model;
using static AutoHouseLottery.Tests.CharacterFixtures;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests;

public sealed class OwnershipGroupTests
{
    [Fact]
    public void UngroupedPrivateCharactersOnOneWorldShareAGroup()
    {
        var groups = OwnershipGroups.Assign([Character(1), Character(2)], [BuyerType.Private, BuyerType.Private]);

        Assert.Equal(groups[0], groups[1]);
    }

    [Fact]
    public void ServiceAccountsSeparatePrivateGroupsOnlyWhenEveryoneIsGrouped()
    {
        var groups = OwnershipGroups.Assign(
            [Character(1, accountGroup: 1), Character(2, accountGroup: 2), Character(3, accountGroup: 1)],
            [BuyerType.Private, BuyerType.Private, BuyerType.Private]);

        Assert.NotEqual(groups[0], groups[1]);
        Assert.Equal(groups[0], groups[2]);
    }

    [Fact]
    public void OneUngroupedCharacterMergesEveryPrivateCharacterOnItsWorld()
    {
        var groups = OwnershipGroups.Assign(
            [Character(1, accountGroup: 1), Character(2, accountGroup: 2), Character(3), Character(4, homeWorld: OtherWorld, accountGroup: 1)],
            [BuyerType.Private, BuyerType.Private, BuyerType.Private, BuyerType.Private]);

        Assert.Equal(groups[0], groups[1]);
        Assert.Equal(groups[0], groups[2]);
        Assert.Equal(OwnershipGroupKind.UngroupedHomeWorld, groups[0].Kind);
        Assert.NotEqual(groups[0], groups[3]);
        Assert.Equal(OwnershipGroupKind.ServiceAccount, groups[3].Kind);
    }

    [Fact]
    public void KnownFreeCompaniesStaySeparate()
    {
        var groups = OwnershipGroups.Assign(
            [Character(1, accountGroup: 1), Character(2, accountGroup: 2), Character(3) with { FreeCompanyId = 901 }],
            [BuyerType.FreeCompany, BuyerType.FreeCompany, BuyerType.FreeCompany]);

        Assert.Equal(OwnershipGroupKind.FreeCompany, groups[0].Kind);
        Assert.Equal(groups[0], groups[1]);
        Assert.NotEqual(groups[0], groups[2]);
    }

    [Fact]
    public void UnknownFreeCompanyMergesWithTheKnownCompaniesOnItsWorld()
    {
        var groups = OwnershipGroups.Assign(
            [Character(1), Character(2) with { FreeCompanyId = 901 }, Character(3) with { FreeCompanyId = null }, Character(4, homeWorld: OtherWorld)],
            [BuyerType.FreeCompany, BuyerType.FreeCompany, BuyerType.FreeCompany, BuyerType.FreeCompany]);

        Assert.Equal(OwnershipGroupKind.FreeCompaniesOnHomeWorld, groups[0].Kind);
        Assert.Equal(groups[0], groups[1]);
        Assert.Equal(groups[0], groups[2]);
        Assert.Equal(OwnershipGroupKind.FreeCompany, groups[3].Kind);
    }

    [Fact]
    public void FreeCompanyAndPrivateEntriesAreDifferentGroups()
    {
        var groups = OwnershipGroups.Assign([Character(1), Character(2)], [BuyerType.FreeCompany, BuyerType.Private]);

        Assert.NotEqual(groups[0], groups[1]);
    }
}
