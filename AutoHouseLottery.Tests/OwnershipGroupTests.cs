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
        Assert.Equal(OwnershipGroups.For(Character(1), BuyerType.Private), OwnershipGroups.For(Character(2), BuyerType.Private));
    }

    [Fact]
    public void ServiceAccountsAndWorldsSeparatePrivateGroups()
    {
        var firstAccount = OwnershipGroups.For(Character(1, accountGroup: 1), BuyerType.Private);

        Assert.NotEqual(firstAccount, OwnershipGroups.For(Character(2, accountGroup: 2), BuyerType.Private));
        Assert.NotEqual(firstAccount, OwnershipGroups.For(Character(3, homeWorld: OtherWorld, accountGroup: 1), BuyerType.Private));
        Assert.Equal(firstAccount, OwnershipGroups.For(Character(4, accountGroup: 1), BuyerType.Private));
    }

    [Fact]
    public void FreeCompanyPlotsGroupByFreeCompany()
    {
        var company = OwnershipGroups.For(Character(1, accountGroup: 1), BuyerType.FreeCompany);

        Assert.Equal(OwnershipGroupKind.FreeCompany, company.Kind);
        Assert.Equal(company, OwnershipGroups.For(Character(2, accountGroup: 2), BuyerType.FreeCompany));
        Assert.NotEqual(company, OwnershipGroups.For(Character(3) with { FreeCompanyId = 901 }, BuyerType.FreeCompany));
    }
}
