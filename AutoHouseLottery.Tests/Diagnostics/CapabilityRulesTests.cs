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

    [Fact]
    public void ABatchIsDryRunOnlyUntilTheEntryAndTheBatchAreBothValidatedAndHasNoPermittedState()
    {
        var entryOnly = Evaluate(Ready() with { EntryConfirmValidated = true, BatchEntryValidated = false, EntryPermitted = true });
        var both = Evaluate(Ready() with { EntryConfirmValidated = true, BatchEntryValidated = true });

        Assert.Equal(new Capability(CapabilityKind.BatchEntry, CapabilityState.DryRunOnly, CapabilityReason.BatchDryRunOnly), entryOnly[(int)CapabilityKind.BatchEntry]);
        Assert.Equal(CapabilityState.Ready, both[(int)CapabilityKind.BatchEntry].State);
    }

    [Fact]
    public void SwitchingIsOffUntilValidatedAndShowsTheSessionPermissionsThatAreOpen()
    {
        var withChange = fullLifestream with { ChangeCharacter = true };
        var off = Evaluate(Ready() with { Lifestream = withChange });
        var batch = Evaluate(Ready() with { Lifestream = withChange, BatchSwitchPermitted = true });
        var refunds = Evaluate(Ready() with { Lifestream = withChange, RefundSwitchPermitted = true });
        var bothGrants = Evaluate(Ready() with { Lifestream = withChange, BatchSwitchPermitted = true, RefundSwitchPermitted = true });
        var noChange = Evaluate(Ready() with { Lifestream = withChange with { ChangeCharacter = false }, BatchSwitchPermitted = true });
        var validated = Evaluate(Ready() with { Lifestream = withChange, CharacterSwitchValidated = true });

        Assert.Equal(new Capability(CapabilityKind.CharacterSwitch, CapabilityState.DryRunOnly, CapabilityReason.SwitchOffUntilValidated), off[(int)CapabilityKind.CharacterSwitch]);
        Assert.Equal(new Capability(CapabilityKind.CharacterSwitch, CapabilityState.PermittedThisSession, CapabilityReason.SwitchPermittedBatch), batch[(int)CapabilityKind.CharacterSwitch]);
        Assert.Equal(CapabilityReason.SwitchPermittedRefunds, refunds[(int)CapabilityKind.CharacterSwitch].Reason);
        Assert.Equal(CapabilityReason.SwitchPermittedBoth, bothGrants[(int)CapabilityKind.CharacterSwitch].Reason);
        Assert.Equal(new Capability(CapabilityKind.CharacterSwitch, CapabilityState.Missing, CapabilityReason.SwitchLifestreamMissing), noChange[(int)CapabilityKind.CharacterSwitch]);
        Assert.Equal(CapabilityState.Ready, validated[(int)CapabilityKind.CharacterSwitch].State);
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
