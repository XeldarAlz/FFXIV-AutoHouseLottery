namespace AutoHouseLottery.Core.Game.Model;

// The residential districts a character has unlocked through their quests, one bit per entry of ResidentialDistricts.All.
internal readonly record struct DistrictAccess(byte UnlockedMask)
{
    public static readonly DistrictAccess None = new(0);

    public DistrictAccess Unlock(int districtIndex) => new((byte)(UnlockedMask | (1 << districtIndex)));

    public bool IsUnlocked(uint territoryTypeId)
    {
        var districtIndex = ResidentialDistricts.IndexOfTerritory(territoryTypeId);
        return districtIndex != ResidentialDistricts.NotResidential && (UnlockedMask & (1 << districtIndex)) != 0;
    }

    // A character already standing in the district got there some other way, such as an estate invitation, so it is not refused.
    public bool CanReach(uint territoryTypeId, uint currentTerritoryTypeId)
        => IsUnlocked(territoryTypeId) || (ResidentialDistricts.IsResidential(territoryTypeId) && territoryTypeId == currentTerritoryTypeId);
}
