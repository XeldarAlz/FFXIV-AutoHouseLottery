using AutoHouseLottery.Core.Diagnostics.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Travel.Model;

namespace AutoHouseLottery.Tests.Diagnostics;

public sealed class CapabilityRulesTests
{
    private static readonly LifestreamCapabilities fullLifestream = new(true, true, true, true, true, true, true, true);

    [Fact]
    public void EveryCapabilityIsReportedOnceInItsOwnSlot()
    {
        var rows = Evaluate(Ready());
        for (var index = 0; index < rows.Length; index++)
        {
            Assert.Equal((CapabilityKind)index, rows[index].Kind);
        }
    }

    [Fact]
    public void AnUnvalidatedActionIsReportedAsDryRunOnly()
    {
        var rows = Evaluate(Ready() with { EntryConfirmValidated = false, RefundValidated = false, ClaimValidated = false, AbsenceSettlementValidated = false });
        Assert.Equal(CapabilityState.DryRunOnly, rows[(int)CapabilityKind.EntryConfirm].State);
        Assert.Equal(CapabilityState.DryRunOnly, rows[(int)CapabilityKind.Refund].State);
        Assert.Equal(CapabilityState.DryRunOnly, rows[(int)CapabilityKind.Claim].State);
        Assert.Equal(CapabilityState.DryRunOnly, rows[(int)CapabilityKind.AbsenceSettlement].State);
        Assert.Equal(CapabilityReason.RefundDryRunOnly, rows[(int)CapabilityKind.Refund].Reason);
    }

    [Fact]
    public void EachValidationFlagTurnsOnlyItsOwnAction()
    {
        var rows = Evaluate(Ready() with { EntryConfirmValidated = false, RefundValidated = true, ClaimValidated = false, AbsenceSettlementValidated = false });
        Assert.Equal(CapabilityState.Ready, rows[(int)CapabilityKind.Refund].State);
        Assert.Equal(CapabilityState.DryRunOnly, rows[(int)CapabilityKind.EntryConfirm].State);
        Assert.Equal(CapabilityState.DryRunOnly, rows[(int)CapabilityKind.Claim].State);
    }

    [Fact]
    public void ASessionPermissionShowsOnTheActionItAllows()
    {
        var rows = Evaluate(Ready() with { EntryConfirmValidated = false, RefundValidated = false, ClaimValidated = false, EntryPermitted = true, ClaimPermitted = true });
        Assert.Equal(CapabilityState.PermittedOnce, rows[(int)CapabilityKind.EntryConfirm].State);
        Assert.Equal(CapabilityReason.EntryPermittedOnce, rows[(int)CapabilityKind.EntryConfirm].Reason);
        Assert.Equal(CapabilityState.DryRunOnly, rows[(int)CapabilityKind.Refund].State);
        Assert.Equal(CapabilityReason.ClaimPermittedOnce, rows[(int)CapabilityKind.Claim].Reason);
    }

    [Fact]
    public void AValidatedActionIsReadyWhetherOrNotAPermissionIsOpen()
    {
        var rows = Evaluate(Ready() with { RefundPermitted = true });
        Assert.Equal(CapabilityState.Ready, rows[(int)CapabilityKind.Refund].State);
    }

    [Fact]
    public void LifestreamThatCannotAnswerTravelIsMissingNotPartial()
    {
        var withoutBusy = Evaluate(Ready() with { Lifestream = fullLifestream with { IsBusy = false } });
        Assert.Equal(CapabilityState.Missing, withoutBusy[(int)CapabilityKind.Lifestream].State);
        Assert.Equal(CapabilityReason.LifestreamCannotTravel, withoutBusy[(int)CapabilityKind.Lifestream].Reason);

        var notInstalled = Evaluate(Ready() with { Lifestream = default });
        Assert.Equal(CapabilityReason.LifestreamMissing, notInstalled[(int)CapabilityKind.Lifestream].Reason);

        var withoutEntrance = Evaluate(Ready() with { Lifestream = fullLifestream with { GetPlotEntrance = false } });
        Assert.Equal(CapabilityState.Fallback, withoutEntrance[(int)CapabilityKind.Lifestream].State);
    }

    [Fact]
    public void ADialogAnsweringPluginOrAnUnwritableJournalBlocks()
    {
        var rows = Evaluate(Ready() with { AutoAnswerPlugin = "YesAlready", JournalWritable = false });
        Assert.Equal(CapabilityState.Blocked, rows[(int)CapabilityKind.AutoAnswer].State);
        Assert.Equal(CapabilityState.Blocked, rows[(int)CapabilityKind.Journal].State);
    }

    [Fact]
    public void ARegionIsJudgedOnlyOnceACharacterIsLoggedIn()
    {
        Assert.Equal(CapabilityState.NotChecked, Evaluate(Ready() with { LoggedIn = false, HomeRegion = WorldRegion.Unknown })[(int)CapabilityKind.Region].State);
        Assert.Equal(CapabilityState.Blocked, Evaluate(Ready() with { HomeRegion = WorldRegion.Korea })[(int)CapabilityKind.Region].State);
        Assert.Equal(CapabilityState.Blocked, Evaluate(Ready() with { HomeRegion = WorldRegion.Unknown })[(int)CapabilityKind.Region].State);
        Assert.Equal(CapabilityState.Ready, Evaluate(Ready())[(int)CapabilityKind.Region].State);
    }

    [Fact]
    public void ReadersThatFellBackSayHowTheyStillWork()
    {
        var rows = Evaluate(Ready() with { LotteryRecordValidated = false, PlacardSale = PlacardSaleFact.NotFound, WardPacketsActive = false, NavmeshAvailable = false });
        Assert.Equal(CapabilityReason.RecordTimersText, rows[(int)CapabilityKind.LotteryRecord].Reason);
        Assert.Equal(CapabilityState.Fallback, rows[(int)CapabilityKind.PlacardSale].State);
        Assert.Equal(CapabilityState.Fallback, rows[(int)CapabilityKind.WardPackets].State);
        Assert.Equal(CapabilityState.Fallback, rows[(int)CapabilityKind.Navmesh].State);
    }

    [Fact]
    public void AWardListNotOpenedYetIsNotCheckedRatherThanReady()
    {
        Assert.Equal(CapabilityState.NotChecked, Evaluate(Ready() with { WardList = WardListFact.NotSeen })[(int)CapabilityKind.WardList].State);
        Assert.Equal(CapabilityState.Missing, Evaluate(Ready() with { WardList = WardListFact.LayoutMismatch })[(int)CapabilityKind.WardList].State);
    }

    [Fact]
    public void ListingsWithoutAnyDataAreMissingButASavedCopyStillServes()
    {
        Assert.Equal(CapabilityState.Missing, Evaluate(Ready() with { Listings = ListingsFact.Unavailable })[(int)CapabilityKind.Listings].State);
        Assert.Equal(CapabilityState.Fallback, Evaluate(Ready() with { Listings = ListingsFact.SavedCopy })[(int)CapabilityKind.Listings].State);
    }

    private static Capability[] Evaluate(in CapabilityFacts facts)
    {
        var rows = new Capability[CapabilityRules.Count];
        CapabilityRules.Evaluate(facts, rows);
        return rows;
    }

    private static CapabilityFacts Ready() => new()
    {
        Listings = ListingsFact.Live,
        LoggedIn = true,
        HomeRegion = WorldRegion.Europe,
        JournalWritable = true,
        Lifestream = fullLifestream,
        LifestreamVersion = "1.0.0.0",
        NavmeshAvailable = true,
        LotteryRecordValidated = true,
        GameVersion = "2026.10.01.0000.0000",
        PlacardSale = PlacardSaleFact.Active,
        WardList = WardListFact.Read,
        WardPacketsActive = true,
        EntryConfirmValidated = true,
        RefundValidated = true,
        ClaimValidated = true,
        AbsenceSettlementValidated = true,
    };
}
