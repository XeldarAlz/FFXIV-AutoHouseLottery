namespace AutoHouseLottery.Core.Localization;

internal static partial class L
{
    internal static class Saved
    {
        public static readonly LocString Title = new("saved.title", "Saved");
        public static readonly LocString Subtitle = new("saved.subtitle", "Searches and plot lists you keep for the next lottery cycle.");
        public static readonly LocString Empty = new("saved.empty", "Searches and plot lists you save will appear here.");

        public static readonly LocString Notify = new("saved.notify", "Notify me");
        public static readonly LocString NotifyHelp = new("saved.notifyHelp", "Show a notification when a saved search finds a new plot, or a saved plot opens for entry, is about to close or has its results. Notices are kept here either way.");
        public static readonly LocString Notices = new("saved.notices", "Notices");
        public static readonly LocString ClearNotices = new("saved.clearNotices", "Clear notices");
        public static readonly LocString Searches = new("saved.searches", "Searches");
        public static readonly LocString SearchesHelp = new("saved.searchesHelp", "A search keeps its areas and filters and finds the plots that match each time you use it.");
        public static readonly LocString Lists = new("saved.lists", "Plot lists");
        public static readonly LocString ListsHelp = new("saved.listsHelp", "A list keeps exactly the plots it was saved with.");
        public static readonly LocPlural MatchingNow = new("saved.matchingNow", "{0} plot matches now", "{0} plots match now");
        public static readonly LocPlural EntryOpenNow = new("saved.entryOpenNow", "{0} open for entry", "{0} open for entry");
        public static readonly LocString ShowOnFind = new("saved.showOnFind", "Show on Find a home");
        public static readonly LocString AddMatches = new("saved.addMatches", "Add matches · {0}");
        public static readonly LocString AddPlots = new("saved.addPlots", "Add to shortlist · {0}");
        public static readonly LocString ShowPlots = new("saved.showPlots", "Show plots");
        public static readonly LocString HidePlots = new("saved.hidePlots", "Hide plots");
        public static readonly LocString Delete = new("saved.delete", "Delete");
        public static readonly LocString ConfirmDelete = new("saved.confirmDelete", "Click again to delete");
        public static readonly LocString SavedOn = new("saved.savedOn", "Saved {0}");
        public static readonly LocString PriceUpTo = new("saved.priceUpTo", "up to {0}");
        public static readonly LocString EmptyList = new("saved.emptyList", "This list has no plots left.");

        public static readonly LocString NoticeNewMatch = new("saved.notice.newMatch", "New match for {0}: {1}");
        public static readonly LocString NoticeOpened = new("saved.notice.opened", "Open for entry: {0}");
        public static readonly LocString NoticeClosing = new("saved.notice.closing", "Entry closes within {0} minutes: {1}");
        public static readonly LocString NoticeResults = new("saved.notice.results", "Lottery results are in: {0}");
        public static readonly LocPlural NoticeSummary = new("saved.notice.summary", "{0} saved plot update", "{0} saved plot updates");
        public static readonly LocString NoticeSummaryBody = new("saved.notice.summaryBody", "Open the Saved page to see them.");
    }
}
