using System.Globalization;
using AutoHouseLottery.Core.Diagnostics.Model;
using AutoHouseLottery.Core.External;
using AutoHouseLottery.Core.Localization;

namespace AutoHouseLottery.Core.Diagnostics;

// The words of a capability: in the plugin's language for the Plugins page, in English for the support report.
internal static class CapabilityText
{
    private const string UnknownDetail = "?";

    public static string Name(CapabilityKind kind, bool english) => kind switch
    {
        CapabilityKind.Lifestream => ExternalPlugins.Catalog[ExternalPlugin.Lifestream].DisplayName,
        CapabilityKind.Navmesh => ExternalPlugins.Catalog[ExternalPlugin.Vnavmesh].DisplayName,
        _ => Words(NameOf(kind), english),
    };

    public static string State(CapabilityState state, bool english) => Words(state switch
    {
        CapabilityState.Ready => L.Diagnostics.StateReady,
        CapabilityState.Fallback => L.Diagnostics.StateFallback,
        CapabilityState.DryRunOnly => L.Diagnostics.StateDryRunOnly,
        CapabilityState.NotChecked => L.Diagnostics.StateNotChecked,
        CapabilityState.Missing => L.Diagnostics.StateMissing,
        _ => L.Diagnostics.StateBlocked,
    }, english);

    public static string Reason(CapabilityReason reason, in CapabilityFacts facts, bool english)
    {
        var template = Words(ReasonOf(reason), english);
        var detail = reason switch
        {
            CapabilityReason.LifestreamReady or CapabilityReason.LifestreamPartial or CapabilityReason.LifestreamCannotTravel => facts.LifestreamVersion,
            CapabilityReason.AutoAnswerLoaded => facts.AutoAnswerPlugin,
            CapabilityReason.RecordTimersText => facts.GameVersion,
            _ => null,
        };

        return template.Contains("{0}", StringComparison.Ordinal)
            ? string.Format(english ? CultureInfo.InvariantCulture : Loc.Culture, template, detail ?? UnknownDetail)
            : template;
    }

    private static string Words(LocString entry, bool english) => english ? entry.Source : Loc.T(entry);

    private static LocString NameOf(CapabilityKind kind) => kind switch
    {
        CapabilityKind.Listings => L.Diagnostics.NameListings,
        CapabilityKind.Region => L.Diagnostics.NameRegion,
        CapabilityKind.Journal => L.Diagnostics.NameJournal,
        CapabilityKind.AutoAnswer => L.Diagnostics.NameAutoAnswer,
        CapabilityKind.LotteryRecord => L.Diagnostics.NameLotteryRecord,
        CapabilityKind.PlacardSale => L.Diagnostics.NamePlacardSale,
        CapabilityKind.WardList => L.Diagnostics.NameWardList,
        CapabilityKind.WardPackets => L.Diagnostics.NameWardPackets,
        CapabilityKind.EntryConfirm => L.Diagnostics.NameEntryConfirm,
        CapabilityKind.Refund => L.Diagnostics.NameRefund,
        CapabilityKind.Claim => L.Diagnostics.NameClaim,
        _ => L.Diagnostics.NameAbsence,
    };

    private static LocString ReasonOf(CapabilityReason reason) => reason switch
    {
        CapabilityReason.ListingsLive => L.Diagnostics.ListingsLive,
        CapabilityReason.ListingsSavedCopy => L.Diagnostics.ListingsSavedCopy,
        CapabilityReason.ListingsUnavailable => L.Diagnostics.ListingsUnavailable,
        CapabilityReason.ListingsLoading => L.Diagnostics.ListingsLoading,
        CapabilityReason.ListingsNoWorld => L.Diagnostics.ListingsNoWorld,
        CapabilityReason.RegionNotLoggedIn => L.Diagnostics.RegionNotLoggedIn,
        CapabilityReason.RegionSupported => L.Diagnostics.RegionSupported,
        CapabilityReason.RegionUnsupported => L.Diagnostics.RegionUnsupported,
        CapabilityReason.JournalWritable => L.Diagnostics.JournalWritable,
        CapabilityReason.JournalUnavailable => L.Diagnostics.JournalUnavailable,
        CapabilityReason.LifestreamReady => L.Diagnostics.LifestreamReady,
        CapabilityReason.LifestreamPartial => L.Diagnostics.LifestreamPartial,
        CapabilityReason.LifestreamCannotTravel => L.Diagnostics.LifestreamCannotTravel,
        CapabilityReason.LifestreamMissing => L.Diagnostics.LifestreamMissing,
        CapabilityReason.NavmeshReady => L.Diagnostics.NavmeshReady,
        CapabilityReason.NavmeshMissing => L.Diagnostics.NavmeshMissing,
        CapabilityReason.AutoAnswerNone => L.Diagnostics.AutoAnswerNone,
        CapabilityReason.AutoAnswerLoaded => L.Diagnostics.AutoAnswerLoaded,
        CapabilityReason.RecordValidated => L.Diagnostics.RecordValidated,
        CapabilityReason.RecordTimersText => L.Diagnostics.RecordTimersText,
        CapabilityReason.PlacardSaleActive => L.Diagnostics.PlacardSaleActive,
        CapabilityReason.PlacardSaleNotAttached => L.Diagnostics.PlacardSaleNotAttached,
        CapabilityReason.PlacardSaleNotFound => L.Diagnostics.PlacardSaleNotFound,
        CapabilityReason.WardListRead => L.Diagnostics.WardListRead,
        CapabilityReason.WardListNotSeen => L.Diagnostics.WardListNotSeen,
        CapabilityReason.WardListMismatch => L.Diagnostics.WardListMismatch,
        CapabilityReason.WardPacketsActive => L.Diagnostics.WardPacketsActive,
        CapabilityReason.WardPacketsOff => L.Diagnostics.WardPacketsOff,
        CapabilityReason.EntryValidated => L.Diagnostics.EntryValidated,
        CapabilityReason.EntryDryRunOnly => L.Diagnostics.EntryDryRunOnly,
        CapabilityReason.RefundValidated => L.Diagnostics.RefundValidated,
        CapabilityReason.RefundDryRunOnly => L.Diagnostics.RefundDryRunOnly,
        CapabilityReason.ClaimValidated => L.Diagnostics.ClaimValidated,
        CapabilityReason.ClaimDryRunOnly => L.Diagnostics.ClaimDryRunOnly,
        CapabilityReason.AbsenceValidated => L.Diagnostics.AbsenceValidated,
        _ => L.Diagnostics.AbsenceDryRunOnly,
    };
}
