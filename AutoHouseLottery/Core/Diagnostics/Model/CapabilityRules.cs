using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Diagnostics.Model;

// Turns what the plugin can tell about itself into one state and one reason per capability, so the Plugins page and the
// support report say the same thing.
internal static class CapabilityRules
{
    public const int Count = (int)CapabilityKind.CharacterSwitch + 1;

    public static void Evaluate(in CapabilityFacts facts, Span<Capability> destination)
    {
        destination[(int)CapabilityKind.Listings] = Listings(facts.Listings);
        destination[(int)CapabilityKind.Region] = Region(facts);
        destination[(int)CapabilityKind.Journal] = facts.JournalWritable
            ? new(CapabilityKind.Journal, CapabilityState.Ready, CapabilityReason.JournalWritable)
            : new(CapabilityKind.Journal, CapabilityState.Blocked, CapabilityReason.JournalUnavailable);
        destination[(int)CapabilityKind.Lifestream] = Lifestream(facts);
        destination[(int)CapabilityKind.Navmesh] = facts.NavmeshAvailable
            ? new(CapabilityKind.Navmesh, CapabilityState.Ready, CapabilityReason.NavmeshReady)
            : new(CapabilityKind.Navmesh, CapabilityState.Fallback, CapabilityReason.NavmeshMissing);
        destination[(int)CapabilityKind.AutoAnswer] = facts.AutoAnswerPlugin is null
            ? new(CapabilityKind.AutoAnswer, CapabilityState.Ready, CapabilityReason.AutoAnswerNone)
            : new(CapabilityKind.AutoAnswer, CapabilityState.Blocked, CapabilityReason.AutoAnswerLoaded);
        destination[(int)CapabilityKind.LotteryRecord] = facts.LotteryRecordValidated
            ? new(CapabilityKind.LotteryRecord, CapabilityState.Ready, CapabilityReason.RecordValidated)
            : new(CapabilityKind.LotteryRecord, CapabilityState.Fallback, CapabilityReason.RecordTimersText);
        destination[(int)CapabilityKind.PlacardSale] = facts.PlacardSale switch
        {
            PlacardSaleFact.Active => new(CapabilityKind.PlacardSale, CapabilityState.Ready, CapabilityReason.PlacardSaleActive),
            PlacardSaleFact.FoundNotAttached => new(CapabilityKind.PlacardSale, CapabilityState.Fallback, CapabilityReason.PlacardSaleNotAttached),
            _ => new(CapabilityKind.PlacardSale, CapabilityState.Fallback, CapabilityReason.PlacardSaleNotFound),
        };
        destination[(int)CapabilityKind.WardList] = facts.WardList switch
        {
            WardListFact.Read => new(CapabilityKind.WardList, CapabilityState.Ready, CapabilityReason.WardListRead),
            WardListFact.LayoutMismatch => new(CapabilityKind.WardList, CapabilityState.Missing, CapabilityReason.WardListMismatch),
            _ => new(CapabilityKind.WardList, CapabilityState.NotChecked, CapabilityReason.WardListNotSeen),
        };
        destination[(int)CapabilityKind.WardPackets] = facts.WardPacketsActive
            ? new(CapabilityKind.WardPackets, CapabilityState.Ready, CapabilityReason.WardPacketsActive)
            : new(CapabilityKind.WardPackets, CapabilityState.Fallback, CapabilityReason.WardPacketsOff);
        destination[(int)CapabilityKind.EntryConfirm] = facts.EntryPermitted && !facts.EntryConfirmValidated
            ? new(CapabilityKind.EntryConfirm, CapabilityState.PermittedOnce, CapabilityReason.EntryPermittedOnce)
            : Validation(CapabilityKind.EntryConfirm, facts.EntryConfirmValidated, CapabilityReason.EntryValidated, CapabilityReason.EntryDryRunOnly);
        destination[(int)CapabilityKind.Refund] = facts.RefundPermitted && !facts.RefundValidated
            ? new(CapabilityKind.Refund, CapabilityState.PermittedOnce, CapabilityReason.RefundPermittedOnce)
            : Validation(CapabilityKind.Refund, facts.RefundValidated, CapabilityReason.RefundValidated, CapabilityReason.RefundDryRunOnly);
        destination[(int)CapabilityKind.Claim] = facts.ClaimPermitted && !facts.ClaimValidated
            ? new(CapabilityKind.Claim, CapabilityState.PermittedOnce, CapabilityReason.ClaimPermittedOnce)
            : Validation(CapabilityKind.Claim, facts.ClaimValidated, CapabilityReason.ClaimValidated, CapabilityReason.ClaimDryRunOnly);
        destination[(int)CapabilityKind.AbsenceSettlement] = Validation(CapabilityKind.AbsenceSettlement, facts.AbsenceSettlementValidated, CapabilityReason.AbsenceValidated, CapabilityReason.AbsenceDryRunOnly);
        destination[(int)CapabilityKind.BatchEntry] = Validation(CapabilityKind.BatchEntry, facts.EntryConfirmValidated && facts.BatchEntryValidated, CapabilityReason.BatchValidated, CapabilityReason.BatchDryRunOnly);
        destination[(int)CapabilityKind.CharacterSwitch] = CharacterSwitch(facts);
    }

    // A batch never goes real on a one-plot permission, so it has no permitted state; switching does, for a while.
    private static Capability CharacterSwitch(in CapabilityFacts facts)
    {
        var permitted = facts.BatchSwitchPermitted || facts.RefundSwitchPermitted;
        if ((facts.CharacterSwitchValidated || permitted) && !facts.Lifestream.CanSwitchCharacters)
        {
            return new(CapabilityKind.CharacterSwitch, CapabilityState.Missing, CapabilityReason.SwitchLifestreamMissing);
        }

        if (facts.CharacterSwitchValidated)
        {
            return new(CapabilityKind.CharacterSwitch, CapabilityState.Ready, CapabilityReason.SwitchValidated);
        }

        if (!permitted)
        {
            return new(CapabilityKind.CharacterSwitch, CapabilityState.DryRunOnly, CapabilityReason.SwitchOffUntilValidated);
        }

        var reason = facts.BatchSwitchPermitted && facts.RefundSwitchPermitted ? CapabilityReason.SwitchPermittedBoth
            : facts.BatchSwitchPermitted ? CapabilityReason.SwitchPermittedBatch
            : CapabilityReason.SwitchPermittedRefunds;
        return new(CapabilityKind.CharacterSwitch, CapabilityState.PermittedThisSession, reason);
    }

    private static Capability Listings(ListingsFact listings) => listings switch
    {
        ListingsFact.Live => new(CapabilityKind.Listings, CapabilityState.Ready, CapabilityReason.ListingsLive),
        ListingsFact.SavedCopy => new(CapabilityKind.Listings, CapabilityState.Fallback, CapabilityReason.ListingsSavedCopy),
        ListingsFact.Unavailable => new(CapabilityKind.Listings, CapabilityState.Missing, CapabilityReason.ListingsUnavailable),
        ListingsFact.Loading => new(CapabilityKind.Listings, CapabilityState.NotChecked, CapabilityReason.ListingsLoading),
        _ => new(CapabilityKind.Listings, CapabilityState.NotChecked, CapabilityReason.ListingsNoWorld),
    };

    private static Capability Region(in CapabilityFacts facts)
    {
        if (!facts.LoggedIn)
        {
            return new(CapabilityKind.Region, CapabilityState.NotChecked, CapabilityReason.RegionNotLoggedIn);
        }

        return WorldRegions.Supported(facts.HomeRegion)
            ? new(CapabilityKind.Region, CapabilityState.Ready, CapabilityReason.RegionSupported)
            : new(CapabilityKind.Region, CapabilityState.Blocked, CapabilityReason.RegionUnsupported);
    }

    // Travel needs the request and the busy flag; the other gates sharpen the arrival check and the walk to the placard.
    private static Capability Lifestream(in CapabilityFacts facts)
    {
        var lifestream = facts.Lifestream;
        if (!lifestream.Installed)
        {
            return new(CapabilityKind.Lifestream, CapabilityState.Missing, CapabilityReason.LifestreamMissing);
        }

        if (!lifestream.CanTravel)
        {
            return new(CapabilityKind.Lifestream, CapabilityState.Missing, CapabilityReason.LifestreamCannotTravel);
        }

        var complete = lifestream.Abort && lifestream.IsHere && lifestream.IsQuickTravelAvailable && lifestream.GetPlotEntrance && lifestream.GetCurrentPlotInfo;
        return complete
            ? new(CapabilityKind.Lifestream, CapabilityState.Ready, CapabilityReason.LifestreamReady)
            : new(CapabilityKind.Lifestream, CapabilityState.Fallback, CapabilityReason.LifestreamPartial);
    }

    private static Capability Validation(CapabilityKind kind, bool validated, CapabilityReason validatedReason, CapabilityReason dryRunReason)
        => validated ? new(kind, CapabilityState.Ready, validatedReason) : new(kind, CapabilityState.DryRunOnly, dryRunReason);
}
