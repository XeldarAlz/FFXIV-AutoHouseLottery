using AutoHouseLottery.Core.Game.Model;

namespace AutoHouseLottery.Tests.Game;

public sealed class DistrictAccessTests
{
    private const uint LimsaLominsaLowerDecks = 129;

    [Theory]
    [InlineData(339u, 66750u)]
    [InlineData(340u, 66748u)]
    [InlineData(341u, 66749u)]
    [InlineData(641u, 68167u)]
    [InlineData(979u, 69708u)]
    public void EachDistrictIsOpenedByItsOwnQuest(uint territoryTypeId, uint questRowId)
    {
        Assert.True(ResidentialDistricts.TryFindByTerritory(territoryTypeId, out var district));
        Assert.Equal(questRowId, district.UnlockQuestId);
    }

    [Fact]
    public void ACharacterWithNoQuestDoneHasNoDistrict()
    {
        for (var districtIndex = 0; districtIndex < ResidentialDistricts.All.Length; districtIndex++)
        {
            Assert.False(DistrictAccess.None.IsUnlocked(ResidentialDistricts.All[districtIndex].TerritoryTypeId));
        }
    }

    [Fact]
    public void OnlyTheUnlockedDistrictsAreOpen()
    {
        var access = DistrictAccess.None.Unlock(IndexOf(ResidentialDistricts.Goblet)).Unlock(IndexOf(ResidentialDistricts.Empyreum));

        Assert.True(access.IsUnlocked(ResidentialDistricts.Goblet.TerritoryTypeId));
        Assert.True(access.IsUnlocked(ResidentialDistricts.Empyreum.TerritoryTypeId));
        Assert.False(access.IsUnlocked(ResidentialDistricts.Mist.TerritoryTypeId));
        Assert.False(access.IsUnlocked(ResidentialDistricts.LavenderBeds.TerritoryTypeId));
        Assert.False(access.IsUnlocked(ResidentialDistricts.Shirogane.TerritoryTypeId));
    }

    [Fact]
    public void ALockedDistrictCannotBeReachedFromElsewhere()
    {
        var access = DistrictAccess.None.Unlock(IndexOf(ResidentialDistricts.Goblet));

        Assert.False(access.CanReach(ResidentialDistricts.Mist.TerritoryTypeId, LimsaLominsaLowerDecks));
        Assert.False(access.CanReach(ResidentialDistricts.Mist.TerritoryTypeId, ResidentialDistricts.Goblet.TerritoryTypeId));
        Assert.True(access.CanReach(ResidentialDistricts.Goblet.TerritoryTypeId, LimsaLominsaLowerDecks));
    }

    [Fact]
    public void StandingInALockedDistrictStillReachesIt()
    {
        Assert.True(DistrictAccess.None.CanReach(ResidentialDistricts.Shirogane.TerritoryTypeId, ResidentialDistricts.Shirogane.TerritoryTypeId));
    }

    [Fact]
    public void ATerritoryThatIsNotADistrictIsNeverReachable()
    {
        var everything = DistrictAccess.None;
        for (var districtIndex = 0; districtIndex < ResidentialDistricts.All.Length; districtIndex++)
        {
            everything = everything.Unlock(districtIndex);
        }

        Assert.Equal(ResidentialDistricts.NotResidential, ResidentialDistricts.IndexOfTerritory(LimsaLominsaLowerDecks));
        Assert.False(everything.IsUnlocked(LimsaLominsaLowerDecks));
        Assert.False(everything.CanReach(LimsaLominsaLowerDecks, LimsaLominsaLowerDecks));
    }

    private static int IndexOf(ResidentialDistrict district) => ResidentialDistricts.IndexOfTerritory(district.TerritoryTypeId);
}
