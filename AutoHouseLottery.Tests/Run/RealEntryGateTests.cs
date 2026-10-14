using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Tasks.Model;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests.Run;

public sealed class RealEntryGateTests
{
    private const ulong Character = 4_000_000_020;
    private const long Ceiling = Deposit;

    [Fact]
    public void CapabilityIsNotValidatedInThisRelease()
    {
        Assert.False(EntryCapabilities.EntryConfirmValidated);
    }

    [Fact]
    public void WithoutValidationOrGrantConfirmIsUnreachableEvenWhenDryRunIsTurnedOff()
    {
        Assert.False(RealEntryGate.MayConfirm(EntryCapabilities.EntryConfirmValidated, dryRunRequested: true, null, RunMode.Exact, Character, Plot(12, 5), Deposit));
        Assert.False(RealEntryGate.MayConfirm(EntryCapabilities.EntryConfirmValidated, dryRunRequested: false, null, RunMode.Exact, Character, Plot(12, 5), Deposit));
        Assert.Equal(EntryMode.DryRun, RealEntryGate.ModeFor(EntryCapabilities.EntryConfirmValidated, false, null, RunMode.Exact, Character, [Plot(12, 5)]));
    }

    [Fact]
    public void GrantOpensConfirmForExactlyItsCharacterPlotAndCeilingOnAnExactRealRun()
    {
        var grant = Grant();

        Assert.True(MayConfirm(grant, Character, Plot(12, 5), Ceiling));
        Assert.True(MayConfirm(grant, Character, Plot(12, 5), Ceiling - 1));
        Assert.False(MayConfirm(grant, Character, Plot(12, 5), Ceiling + 1));
        Assert.False(MayConfirm(grant, Character + 1, Plot(12, 5), Ceiling));
        Assert.False(MayConfirm(grant, Character, Plot(12, 6), Ceiling));
        Assert.False(MayConfirm(grant, Character, Plot(12, 5, OtherWorld), Ceiling));
        Assert.False(MayConfirm(grant, Character, Plot(12, 5), 0));
    }

    [Fact]
    public void RequestedDryRunWinsOverAnyGrantOrCapability()
    {
        var grant = Grant();

        Assert.False(RealEntryGate.MayConfirm(false, dryRunRequested: true, grant, RunMode.Exact, Character, Plot(12, 5), Ceiling));
        Assert.False(RealEntryGate.MayConfirm(true, dryRunRequested: true, null, RunMode.Exact, Character, Plot(12, 5), Ceiling));
        Assert.Equal(EntryMode.DryRun, RealEntryGate.ModeFor(false, true, grant, RunMode.Exact, Character, [Plot(12, 5)]));
        Assert.Equal(EntryMode.DryRun, RealEntryGate.ModeFor(true, true, null, RunMode.Exact, Character, [Plot(12, 5)]));
    }

    [Fact]
    public void GrantNeverAppliesToABestMatch()
    {
        var grant = Grant();

        Assert.False(RealEntryGate.MayConfirm(false, false, grant, RunMode.BestMatch, Character, Plot(12, 5), Ceiling));
        Assert.Equal(EntryMode.DryRun, RealEntryGate.ModeFor(false, false, grant, RunMode.BestMatch, Character, [Plot(12, 5)]));
        Assert.Equal(EntryMode.DryRun, RealEntryGate.ModeFor(false, false, grant, RunMode.BestMatch, Character, [Plot(3, 1), Plot(12, 5)]));
    }

    [Fact]
    public void UsedGrantOpensNothing()
    {
        var used = Grant() with { Used = true };

        Assert.False(MayConfirm(used, Character, Plot(12, 5), Ceiling));
        Assert.Equal(EntryMode.DryRun, RealEntryGate.ModeFor(false, false, used, RunMode.Exact, Character, [Plot(12, 5)]));
    }

    [Fact]
    public void ExactRealRunIsRealOnlyOnTheGrantedPlotAndCharacter()
    {
        var grant = Grant();

        Assert.Equal(EntryMode.Real, RealEntryGate.ModeFor(false, false, grant, RunMode.Exact, Character, [Plot(12, 5)]));
        Assert.Equal(EntryMode.DryRun, RealEntryGate.ModeFor(false, false, grant, RunMode.Exact, Character, [Plot(3, 1)]));
        Assert.Equal(EntryMode.DryRun, RealEntryGate.ModeFor(false, false, grant, RunMode.Exact, Character + 1, [Plot(12, 5)]));
    }

    [Fact]
    public void ValidatedCapabilityStillHonoursADryRunRequest()
    {
        Assert.False(RealEntryGate.MayConfirm(true, dryRunRequested: true, null, RunMode.BestMatch, Character, Plot(12, 5), Deposit));
        Assert.True(RealEntryGate.MayConfirm(true, dryRunRequested: false, null, RunMode.BestMatch, Character, Plot(12, 5), Deposit));
    }

    private static bool MayConfirm(RealEntryGrant grant, ulong contentId, PlotKey plot, long deposit)
        => RealEntryGate.MayConfirm(false, dryRunRequested: false, grant, RunMode.Exact, contentId, plot, deposit);

    private static RealEntryGrant Grant() => new(Character, "Test Character", Plot(12, 5), Ceiling, ObservedAt);
}
