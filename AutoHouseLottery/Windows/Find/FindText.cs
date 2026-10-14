using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Core.Maps;
using System.Text;

namespace AutoHouseLottery.Windows.Find;

// The words the Find a home and Saved pages put on plots. A value nobody reported reads as unknown, never as zero.
internal static class FindText
{
    private const int HoursPerDay = 24;
    private const string LocalTimeFormat = "g";

    public static string Status(PlotStatus? status) => status switch
    {
        PlotStatus.EntryOpen => Loc.T(L.Find.StatusEntryOpen),
        PlotStatus.Results => Loc.T(L.Find.StatusResults),
        PlotStatus.NextCycle => Loc.T(L.Find.StatusNextCycle),
        PlotStatus.ForSale => Loc.T(L.Find.StatusForSale),
        PlotStatus.Occupied => Loc.T(L.Find.StatusOccupied),
        PlotStatus.NotReported => Loc.T(L.Find.StatusNotReported),
        PlotStatus.NeedsRefresh => Loc.T(L.Find.StatusNeedsRefresh),
        _ => Loc.T(L.Find.StatusNotLoaded),
    };

    public static string StatusHelp(PlotStatus status) => status switch
    {
        PlotStatus.EntryOpen => Loc.T(L.Find.StatusEntryOpenHelp),
        PlotStatus.Results => Loc.T(L.Find.StatusResultsHelp),
        PlotStatus.NextCycle => Loc.T(L.Find.StatusNextCycleHelp),
        PlotStatus.ForSale => Loc.T(L.Find.StatusForSaleHelp),
        PlotStatus.Occupied => Loc.T(L.Find.StatusOccupiedHelp),
        PlotStatus.NotReported => Loc.T(L.Find.StatusNotReportedHelp),
        _ => Loc.T(L.Find.StatusNeedsRefreshHelp),
    };

    public static string Size(PlotSize? size) => size switch
    {
        PlotSize.Small => Loc.T(L.Find.SizeSmall),
        PlotSize.Medium => Loc.T(L.Find.SizeMedium),
        PlotSize.Large => Loc.T(L.Find.SizeLarge),
        _ => Loc.T(L.Find.Unknown),
    };

    public static string Buyers(PlotBuyers? buyers) => buyers switch
    {
        PlotBuyers.Private => Loc.T(L.Find.BuyersPrivate),
        PlotBuyers.FreeCompany => Loc.T(L.Find.BuyersFreeCompany),
        PlotBuyers.Both => Loc.T(L.Find.BuyersBoth),
        _ => Loc.T(L.Find.Unknown),
    };

    public static string Gil(long? amount) => amount is { } known ? Formatting.Gil(known) : Loc.T(L.Find.Unknown);

    public static string Count(int? count) => count is { } known ? known.ToString("N0", Loc.Culture) : Loc.T(L.Find.Unknown);

    public static string Age(DateTime observedUtc, DateTime nowUtc) => observedUtc == default ? Loc.T(L.Find.Unknown) : Formatting.RelativeTime(observedUtc, nowUtc);

    public static string Observed(DateTime observedUtc, DateTime nowUtc) => Loc.T(L.Find.Observed, Age(observedUtc, nowUtc));

    public static string LocalTime(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString(LocalTimeFormat, Loc.Culture);

    public static string Countdown(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        if (remaining.TotalHours >= HoursPerDay)
        {
            return Loc.T(L.Find.CountdownDays, (int)remaining.TotalDays, remaining.Hours);
        }

        return remaining.TotalHours >= 1d
            ? Loc.T(L.Find.CountdownHours, (int)remaining.TotalHours, remaining.Minutes)
            : Loc.T(L.Find.CountdownMinutes, Math.Max(0, (int)remaining.TotalMinutes));
    }

    public static string Sort(ResultSort sort) => sort switch
    {
        ResultSort.FewestEntries => Loc.T(L.Find.SortFewest),
        ResultSort.LowestDeposit => Loc.T(L.Find.SortLowest),
        ResultSort.RecentlyObserved => Loc.T(L.Find.SortRecent),
        ResultSort.Address => Loc.T(L.Find.SortAddress),
        _ => Loc.T(L.Find.SortPreferred),
    };

    public static string Source(ObservationSource source) => source switch
    {
        ObservationSource.WardScan => Loc.T(L.Find.SourceWardScan),
        ObservationSource.Placard => Loc.T(L.Find.SourcePlacard),
        _ => Loc.T(L.Find.SourceFeed),
    };

    public static string Districts(GameNames names, in ScopeSummary summary)
    {
        if (summary.AllDistricts)
        {
            return Loc.T(L.Find.ScopeAllDistricts);
        }

        return summary.Districts.Length switch
        {
            1 => names.District(summary.Districts[0]),
            2 => Loc.T(L.Find.ScopeTwoDistricts, names.District(summary.Districts[0]), names.District(summary.Districts[1])),
            _ => Loc.Plural(L.Find.ScopeDistricts, summary.Districts.Length),
        };
    }

    public static string Scope(GameNames names, in AreaScope area)
    {
        var summary = ScopeSummary.Describe(area);
        var wards = summary.Wards switch
        {
            WardSpan.All => Loc.T(L.Find.ScopeAllWards),
            WardSpan.Single => Loc.T(L.Find.ScopeWard, summary.FirstWard),
            WardSpan.Range => Loc.T(L.Find.ScopeWardRange, summary.FirstWard, summary.LastWard),
            _ => Loc.Plural(L.Find.ScopeWards, summary.WardCount),
        };
        return Loc.T(L.Find.ScopeJoin, Districts(names, summary), wards, Loc.T(ScopeWords.Divisions(summary.Divisions)));
    }

    public static string Reasons(EligibilityReason reasons)
    {
        var text = new StringBuilder();
        Append(text, reasons, EligibilityReason.AlreadyEnteredThisCycle, L.Find.ReasonAlreadyEntered);
        Append(text, reasons, EligibilityReason.AccountOwnsLandOnWorld, L.Find.ReasonOwnsLand);
        Append(text, reasons, EligibilityReason.OutstandingRefundOrProperty, L.Find.ReasonOutstanding);
        Append(text, reasons, EligibilityReason.InsufficientGil, L.Find.ReasonGil);
        Append(text, reasons, EligibilityReason.DepositOverCap, L.Find.ReasonOverCap);
        Append(text, reasons, EligibilityReason.WrongHomeWorld, L.Find.ReasonWrongWorld);
        Append(text, reasons, EligibilityReason.BuyerTypeMismatch, L.Find.ReasonBuyer);
        Append(text, reasons, EligibilityReason.BuyerRequirementsNotMet, L.Find.ReasonRequirements);
        Append(text, reasons, EligibilityReason.PlotUnavailable, L.Find.ReasonUnavailable);
        Append(text, reasons, EligibilityReason.ProfileUnavailable, L.Find.ReasonProfile);
        Append(text, reasons, EligibilityReason.PlotNeedsVerification, L.Find.ReasonVerification);
        return text.ToString();
    }

    public static string Hours(int hours) => Loc.Plural(L.Find.Hours, hours);

    private static void Append(StringBuilder text, EligibilityReason reasons, EligibilityReason reason, LocString label)
    {
        if ((reasons & reason) == 0)
        {
            return;
        }

        var part = Loc.T(label);
        if (text.Length == 0)
        {
            text.Append(part);
            return;
        }

        var joined = Loc.T(L.Find.ReasonJoin, text.ToString(), part);
        text.Clear().Append(joined);
    }
}
