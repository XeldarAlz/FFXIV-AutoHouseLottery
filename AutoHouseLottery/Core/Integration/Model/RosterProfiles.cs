using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence.Model;

namespace AutoHouseLottery.Core.Integration.Model;

// What the plugin remembers of a character from its last login. Every field but the name and home world can be unknown.
internal readonly record struct RosterFacts(
    ulong ContentId,
    string Name,
    uint HomeWorldId,
    int? AccountGroup,
    ulong? FreeCompanyId,
    bool? OwnsLandOnHomeWorld,
    long? Gil,
    DateTime? GilSeenUtc,
    DateTime? SeenUtc);

internal static class RosterProfiles
{
    // A character that is not logged in, planned from the journal and its last login. The journal alone decides its entry
    // slot and earlier settlements, since no reading of its lottery record is current; its gil is an estimate until it
    // logs in again, and nothing remembered can make it eligible on its own.
    public static CharacterProfile Offline(in RosterFacts facts, GameRegion region, in JournalSnapshot journal, in CyclePosition position)
    {
        var unread = new SlotEvidence(EntrySlotState.Unknown, null, null, null);
        var participation = ParticipationRules.Resolve(position.Cycle, ParticipationRules.FromJournal(journal, facts.ContentId, facts.HomeWorldId, position.Cycle), unread);
        return new CharacterProfile
        {
            ContentId = facts.ContentId,
            Name = facts.Name,
            Region = region,
            HomeWorldId = facts.HomeWorldId,
            ObservedGil = facts.Gil,
            GilObservedUtc = facts.GilSeenUtc,
            GilIsEstimate = true,
            FreeCompanyId = facts.FreeCompanyId,
            AccountGroup = facts.AccountGroup,
            AccountOwnsLandOnHomeWorld = facts.OwnsLandOnHomeWorld == true ? true : null,
            Outstanding = OutstandingRules.ToSettlement(OutstandingRules.Find(journal, facts.ContentId, position.Cycle).Kind),
            Participation = participation,
            FactsObservedUtc = facts.SeenUtc,
        };
    }
}
