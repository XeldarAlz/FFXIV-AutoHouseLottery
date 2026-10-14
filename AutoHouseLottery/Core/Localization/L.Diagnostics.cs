namespace AutoHouseLottery.Core.Localization;

internal static partial class L
{
    internal static class Diagnostics
    {
        public static readonly LocString Title = new("diagnostics.title", "Compatibility");
        public static readonly LocString Subtitle = new("diagnostics.subtitle", "What works on this game build and with your plugins right now, and why.");
        public static readonly LocString Copy = new("diagnostics.copy", "Copy diagnostics");
        public static readonly LocString CopyTooltip = new("diagnostics.copyTooltip", "Copies the plugin, Dalamud and game versions, your client language, these checks and the last {0} Console lines, with character names and IDs replaced by aliases, ready to paste into a bug report.");
        public static readonly LocString Copied = new("diagnostics.copied", "Copied");

        public static readonly LocString GroupBasics = new("diagnostics.group.basics", "Basics");
        public static readonly LocString GroupHelpers = new("diagnostics.group.helpers", "Helper plugins");
        public static readonly LocString GroupReadings = new("diagnostics.group.readings", "Game readings");
        public static readonly LocString GroupActions = new("diagnostics.group.actions", "Game actions");

        public static readonly LocString NameListings = new("diagnostics.name.listings", "Housing listings");
        public static readonly LocString NameRegion = new("diagnostics.name.region", "Game region");
        public static readonly LocString NameJournal = new("diagnostics.name.journal", "Entry records");
        public static readonly LocString NameAutoAnswer = new("diagnostics.name.autoAnswer", "Plugins that answer dialogs");
        public static readonly LocString NameLotteryRecord = new("diagnostics.name.lotteryRecord", "Your lottery entry");
        public static readonly LocString NamePlacardSale = new("diagnostics.name.placardSale", "Placard sale details");
        public static readonly LocString NameWardList = new("diagnostics.name.wardList", "Ward list window");
        public static readonly LocString NameWardPackets = new("diagnostics.name.wardPackets", "Ward list data");
        public static readonly LocString NameEntryConfirm = new("diagnostics.name.entryConfirm", "Entering the lottery");
        public static readonly LocString NameRefund = new("diagnostics.name.refund", "Collecting refunds");
        public static readonly LocString NameClaim = new("diagnostics.name.claim", "Claiming a house");
        public static readonly LocString NameAbsence = new("diagnostics.name.absence", "Placards without a result");
        public static readonly LocString NameBatchEntry = new("diagnostics.name.batchEntry", "Entering for several characters");
        public static readonly LocString NameCharacterSwitch = new("diagnostics.name.characterSwitch", "Switching characters");

        public static readonly LocString StateReady = new("diagnostics.state.ready", "Ready");
        public static readonly LocString StateFallback = new("diagnostics.state.fallback", "Fallback");
        public static readonly LocString StateDryRunOnly = new("diagnostics.state.dryRunOnly", "Dry run only until validated");
        public static readonly LocString StatePermittedOnce = new("diagnostics.state.permittedOnce", "One real press allowed");
        public static readonly LocString StatePermittedThisSession = new("diagnostics.state.permittedThisSession", "Allowed for now");
        public static readonly LocString StateNotChecked = new("diagnostics.state.notChecked", "Not checked yet");
        public static readonly LocString StateMissing = new("diagnostics.state.missing", "Missing");
        public static readonly LocString StateBlocked = new("diagnostics.state.blocked", "Blocked");

        public static readonly LocString ListingsLive = new("diagnostics.reason.listingsLive", "The housing service answered for the world you browse.");
        public static readonly LocString ListingsSavedCopy = new("diagnostics.reason.listingsSavedCopy", "The housing service did not answer, so Find a home shows the listings saved earlier.");
        public static readonly LocString ListingsUnavailable = new("diagnostics.reason.listingsUnavailable", "The housing service could not be reached and no saved listings exist, so Find a home has nothing to show until it answers.");
        public static readonly LocString ListingsLoading = new("diagnostics.reason.listingsLoading", "Asking the housing service for the world you browse.");
        public static readonly LocString ListingsNoWorld = new("diagnostics.reason.listingsNoWorld", "Log in, or choose a world on Find a home.");
        public static readonly LocString RegionNotLoggedIn = new("diagnostics.reason.regionNotLoggedIn", "Checked once a character is logged in.");
        public static readonly LocString RegionSupported = new("diagnostics.reason.regionSupported", "This character's home world is in a supported region: Japan, North America, Europe or Oceania.");
        public static readonly LocString RegionUnsupported = new("diagnostics.reason.regionUnsupported", "This character's home world is a Chinese or Korean world, or its region could not be read, so browsing and entries are off for it.");
        public static readonly LocString JournalWritable = new("diagnostics.reason.journalWritable", "Every entry, refund and claim is written down before the plugin confirms it.");
        public static readonly LocString JournalUnavailable = new("diagnostics.reason.journalUnavailable", "Your entry records cannot be written, so entries, refunds and claims are paused. My lottery says why.");
        public static readonly LocString LifestreamReady = new("diagnostics.reason.lifestreamReady", "Version {0} travels to a plot for entries, result checks and refunds.");
        public static readonly LocString LifestreamPartial = new("diagnostics.reason.lifestreamPartial", "Version {0} travels, but does not answer every arrival check the plugin uses. Update Lifestream.");
        public static readonly LocString LifestreamCannotTravel = new("diagnostics.reason.lifestreamCannotTravel", "Version {0} is installed but does not take travel requests from other plugins. Update Lifestream.");
        public static readonly LocString LifestreamMissing = new("diagnostics.reason.lifestreamMissing", "Not installed. Browsing and My lottery work without it; entering, result checks and refunds need it to travel.");
        public static readonly LocString NavmeshReady = new("diagnostics.reason.navmeshReady", "Walks the last metres from where travel ends to the plot's placard.");
        public static readonly LocString NavmeshMissing = new("diagnostics.reason.navmeshMissing", "Not installed. When travel ends away from the placard, the plugin asks you to walk up to it.");
        public static readonly LocString AutoAnswerNone = new("diagnostics.reason.autoAnswerNone", "None loaded.");
        public static readonly LocString AutoAnswerLoaded = new("diagnostics.reason.autoAnswerLoaded", "{0} is loaded. It could press Confirm or Yes on the lottery dialogs, so entries, result checks, refunds and claims wait until you turn it off.");
        public static readonly LocString RecordValidated = new("diagnostics.reason.recordValidated", "Read directly on this game build, which has been checked in game.");
        public static readonly LocString RecordTimersText = new("diagnostics.reason.recordTimersText", "Read from the Timers window, which opens for a moment. Game build {0} has not been checked for the direct read.");
        public static readonly LocString PlacardSaleActive = new("diagnostics.reason.placardSaleActive", "Read as the game sends them when a placard opens.");
        public static readonly LocString PlacardSaleNotAttached = new("diagnostics.reason.placardSaleNotAttached", "Could not be watched on this game build. Placards are read from their window instead.");
        public static readonly LocString PlacardSaleNotFound = new("diagnostics.reason.placardSaleNotFound", "Not found on this game build. Placards are read from their window instead.");
        public static readonly LocString WardListRead = new("diagnostics.reason.wardListRead", "A ward list read this session matched the layout the plugin expects.");
        public static readonly LocString WardListNotSeen = new("diagnostics.reason.wardListNotSeen", "Checked the first time a ward list is read this session.");
        public static readonly LocString WardListMismatch = new("diagnostics.reason.wardListMismatch", "The ward list window does not match the layout the plugin expects on this game build, so ward lists are not read. Entries still read each plot's placard.");
        public static readonly LocString WardPacketsActive = new("diagnostics.reason.wardPacketsActive", "The plot data the game sends with each ward list is copied as it arrives.");
        public static readonly LocString WardPacketsOff = new("diagnostics.reason.wardPacketsOff", "Not available on this game build. Ward lists are read from their window alone.");
        public static readonly LocString EntryValidated = new("diagnostics.reason.entryValidated", "Validated in game: Enter submits the entry for the exact deposit it showed.");
        public static readonly LocString EntryDryRunOnly = new("diagnostics.reason.entryDryRunOnly", "Enter goes as far as the lottery confirmation and closes it, so no deposit is paid.");
        public static readonly LocString EntryPermittedOnce = new("diagnostics.reason.entryPermittedOnce", "Dry run only until validated, but a permission given this session allows one real entry on one plot. /ahl dev revoke-real-entry withdraws it.");
        public static readonly LocString RefundValidated = new("diagnostics.reason.refundValidated", "Validated in game: a losing deposit is collected, by itself while automatic refunds are on.");
        public static readonly LocString RefundDryRunOnly = new("diagnostics.reason.refundDryRunOnly", "A result check reads the refund and closes it, so nothing is collected. Collect refunds in game.");
        public static readonly LocString RefundPermittedOnce = new("diagnostics.reason.refundPermittedOnce", "Dry run only until validated, but a permission given this session allows one real refund on one plot. /ahl dev revoke-real-settlement withdraws it.");
        public static readonly LocString ClaimValidated = new("diagnostics.reason.claimValidated", "Validated in game: Claim house accepts the win you chose to claim.");
        public static readonly LocString ClaimDryRunOnly = new("diagnostics.reason.claimDryRunOnly", "A win is read and closed, so nothing is claimed. Claim your house in game.");
        public static readonly LocString ClaimPermittedOnce = new("diagnostics.reason.claimPermittedOnce", "Dry run only until validated, but a permission given this session allows one real claim on one plot. /ahl dev revoke-real-settlement withdraws it.");
        public static readonly LocString AbsenceValidated = new("diagnostics.reason.absenceValidated", "Validated in game: an entry whose placard no longer offers a result is recorded as settled outside the plugin.");
        public static readonly LocString BatchValidated = new("diagnostics.reason.batchValidated", "Validated in game: Enter for N characters makes a real entry for each character.");
        public static readonly LocString BatchDryRunOnly = new("diagnostics.reason.batchDryRunOnly", "Enter for N characters takes each character as far as its lottery confirmation and closes it. A one-plot real-entry permission never makes a batch real.");
        public static readonly LocString SwitchValidated = new("diagnostics.reason.switchValidated", "Validated in game: batches and, when its option is on, the refund queue log in each character through Lifestream.");
        public static readonly LocString SwitchOffUntilValidated = new("diagnostics.reason.switchOffUntilValidated", "Off until validated: each character is processed at its own login.");
        public static readonly LocString SwitchPermittedBatch = new("diagnostics.reason.switchPermittedBatch", "Off until validated, but a permission given this session lets one batch, the one not finished yet or else the next one started, switch characters until it ends, for two hours at most. /ahl dev revoke-switching withdraws it.");
        public static readonly LocString SwitchPermittedRefunds = new("diagnostics.reason.switchPermittedRefunds", "Off until validated, but a permission given this session lets the refund queue switch characters for two hours. /ahl dev revoke-switching withdraws it.");
        public static readonly LocString SwitchPermittedBoth = new("diagnostics.reason.switchPermittedBoth", "Off until validated, but permissions given this session let one batch, the one not finished yet or else the next one started, and the refund queue switch characters, for two hours at most. /ahl dev revoke-switching withdraws them.");
        public static readonly LocString SwitchLifestreamMissing = new("diagnostics.reason.switchLifestreamMissing", "Lifestream does not offer its character change to other plugins, so characters are never switched. Update Lifestream.");
        public static readonly LocString AbsenceDryRunOnly = new("diagnostics.reason.absenceDryRunOnly", "An entry whose placard no longer offers a result stays pending on My lottery instead of being recorded as settled, and does not hold up your next entry.");
    }
}
