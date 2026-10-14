using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Housing.Model;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests;

internal static class CharacterFixtures
{
    public static CharacterProfile Character(ulong contentId, uint homeWorld = HomeWorld, long? gil = 50_000_000, int? accountGroup = null)
    {
        return new CharacterProfile
        {
            ContentId = contentId,
            Name = $"Character {contentId}",
            Region = GameRegion.Global,
            HomeWorldId = homeWorld,
            ObservedGil = gil,
            PrivateBuyerQualified = true,
            FreeCompanyBuyerQualified = true,
            FreeCompanyId = 900,
            AccountGroup = accountGroup,
            AccountOwnsLandOnHomeWorld = false,
            Outstanding = OutstandingSettlement.None,
            Participation = new CycleParticipation(FirstCycle, EntrySlotState.Open),
            ObservedUtc = ObservedAt,
        };
    }
}
