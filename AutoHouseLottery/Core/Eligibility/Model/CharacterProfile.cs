using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Eligibility.Model;

internal readonly record struct CharacterProfile
{
    public const ulong NoFreeCompany = 0;

    public required ulong ContentId { get; init; }

    public required string Name { get; init; }

    public required GameRegion Region { get; init; }

    public required uint HomeWorldId { get; init; }

    public long? ObservedGil { get; init; }

    public DateTime? GilObservedUtc { get; init; }

    // The balance was read at an earlier login, not now: it is shown, but it can neither prove nor disprove that the
    // character can pay until the character logs in again.
    public bool GilIsEstimate { get; init; }

    // Its home world is on a Chinese or Korean data center, or one whose region could not be read (WorldRegions).
    public bool RegionUnsupported { get; init; }

    public bool? PrivateBuyerQualified { get; init; }

    public bool? FreeCompanyBuyerQualified { get; init; }

    public ulong? FreeCompanyId { get; init; }

    public int? AccountGroup { get; init; }

    public bool? AccountOwnsLandOnHomeWorld { get; init; }

    public OutstandingSettlement Outstanding { get; init; }

    public CycleParticipation Participation { get; init; }

    public DateTime? FactsObservedUtc { get; init; }
}
