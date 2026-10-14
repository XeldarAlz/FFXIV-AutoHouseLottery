using AutoHouseLottery.Core.Eligibility.Model;

namespace AutoHouseLottery.Core.Find.Model;

// The parts of the character's profile the Find a home view is planned from, without its gil: the view takes a changed
// balance on its minute cadence, so a gil change alone does not rebuild every result.
internal readonly record struct CharacterKey(
    ulong ContentId,
    uint HomeWorldId,
    ulong? FreeCompanyId,
    int? AccountGroup,
    bool? AccountOwnsLandOnHomeWorld,
    OutstandingSettlement Outstanding,
    CycleParticipation Participation)
{
    public static CharacterKey? Of(CharacterProfile? character) => character is { } profile
        ? new CharacterKey(
            profile.ContentId,
            profile.HomeWorldId,
            profile.FreeCompanyId,
            profile.AccountGroup,
            profile.AccountOwnsLandOnHomeWorld,
            profile.Outstanding,
            profile.Participation)
        : null;
}
