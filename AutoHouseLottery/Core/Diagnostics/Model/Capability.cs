using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Travel.Model;

namespace AutoHouseLottery.Core.Diagnostics.Model;

// Every capability the Plugins page reports, in the order it lists them.
internal enum CapabilityKind : byte
{
    Listings,
    Region,
    Journal,
    Lifestream,
    Navmesh,
    AutoAnswer,
    LotteryRecord,
    PlacardSale,
    WardList,
    WardPackets,
}

// Fallback: it works through a slower or less direct path. NotChecked: nothing has told yet, such as a window not opened
// this session.
internal enum CapabilityState : byte
{
    Ready,
    Fallback,
    NotChecked,
    Missing,
    Blocked,
}

internal enum CapabilityReason : byte
{
    ListingsLive,
    ListingsSavedCopy,
    ListingsUnavailable,
    ListingsLoading,
    ListingsNoWorld,
    RegionNotLoggedIn,
    RegionSupported,
    RegionUnsupported,
    JournalWritable,
    JournalUnavailable,
    LifestreamReady,
    LifestreamPartial,
    LifestreamCannotTravel,
    LifestreamMissing,
    NavmeshReady,
    NavmeshMissing,
    AutoAnswerNone,
    AutoAnswerPaused,
    AutoAnswerLoaded,
    RecordValidated,
    RecordTimersText,
    PlacardSaleActive,
    PlacardSaleNotAttached,
    PlacardSaleNotFound,
    WardListRead,
    WardListNotSeen,
    WardListMismatch,
    WardPacketsActive,
    WardPacketsOff,
}

internal readonly record struct Capability(CapabilityKind Kind, CapabilityState State, CapabilityReason Reason);

internal enum ListingsFact : byte
{
    NoWorld,
    Loading,
    Live,
    SavedCopy,
    Unavailable,
}

internal enum PlacardSaleFact : byte
{
    Active,
    FoundNotAttached,
    NotFound,
}

internal enum WardListFact : byte
{
    NotSeen,
    Read,
    LayoutMismatch,
}

// What the plugin can tell about itself right now. The details (versions, the auto-answer plugin's name) only feed the
// words of a reason; the rules decide on the facts alone.
internal readonly record struct CapabilityFacts
{
    public ListingsFact Listings { get; init; }

    public bool LoggedIn { get; init; }

    public WorldRegion HomeRegion { get; init; }

    public bool JournalWritable { get; init; }

    public LifestreamCapabilities Lifestream { get; init; }

    public string? LifestreamVersion { get; init; }

    public bool NavmeshAvailable { get; init; }

    public string? AutoAnswerPlugin { get; init; }

    public bool AutoAnswerPausable { get; init; }

    public bool LotteryRecordValidated { get; init; }

    public string? GameVersion { get; init; }

    public PlacardSaleFact PlacardSale { get; init; }

    public WardListFact WardList { get; init; }

    public bool WardPacketsActive { get; init; }
}
