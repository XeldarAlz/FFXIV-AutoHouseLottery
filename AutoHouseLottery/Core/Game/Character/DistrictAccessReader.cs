using AutoHouseLottery.Core.Game.Model;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AutoHouseLottery.Core.Game.Character;

internal static class DistrictAccessReader
{
    public static DistrictAccess Read()
    {
        var access = DistrictAccess.None;
        var districts = ResidentialDistricts.All;
        for (var districtIndex = 0; districtIndex < districts.Length; districtIndex++)
        {
            if (QuestManager.IsQuestComplete(districts[districtIndex].UnlockQuestId))
            {
                access = access.Unlock(districtIndex);
            }
        }

        return access;
    }
}
