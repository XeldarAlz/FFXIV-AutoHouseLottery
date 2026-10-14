using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Integration.Model;

internal static class CharacterProfileMapping
{
    // The game shows nothing about the buyer requirements (rank, the Free Company's 30 days), so they stay unknown and
    // the game's own refusal is what settles them.
    public static CharacterProfile From(CharacterContext context, GameRegion region, int? accountGroup, OutstandingSettlement outstanding, in CycleParticipation participation)
    {
        return new CharacterProfile
        {
            ContentId = context.ContentId,
            Name = context.Name,
            Region = region,
            HomeWorldId = context.HomeWorldId,
            ObservedGil = context.Gil,
            GilObservedUtc = context.Gil is null ? null : context.ReadAtUtc,
            FreeCompanyId = context.FreeCompanyId,
            AccountGroup = accountGroup,
            AccountOwnsLandOnHomeWorld = OwnsLandOnHomeWorld(context),
            Outstanding = outstanding,
            Participation = participation,
            FactsObservedUtc = context.ReadAtUtc,
        };
    }

    // The character's own house on its home world proves its account holds land there. Nothing the client shows proves
    // the opposite, since another character of the same account may own it.
    public static bool? OwnsLandOnHomeWorld(CharacterContext context)
        => context.PersonalEstate is { Apartment: false } estate && estate.WorldId == context.HomeWorldId ? true : null;
}
