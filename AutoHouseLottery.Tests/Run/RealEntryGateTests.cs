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
        Assert.False(RealEntryGate.MayConfirm(EntryCapabilities.EntryConfirmValidated, dryRunRequested: true, null, Character, Plot(12, 5), Deposit));
        Assert.False(RealEntryGate.MayConfirm(EntryCapabilities.EntryConfirmValidated, dryRunRequested: false, null, Character, Plot(12, 5), Deposit));
        Assert.Equal(EntryMode.DryRun, RealEntryGate.ModeFor(EntryCapabilities.EntryConfirmValidated, false, null, Character, [Plot(12, 5)]));
    }

    [Fact]
    public void GrantOpensConfirmForExactlyItsCharacterPlotAndCeiling()
    {
        var grant = Grant();

        Assert.True(RealEntryGate.MayConfirm(false, true, grant, Character, Plot(12, 5), Ceiling));
        Assert.True(RealEntryGate.MayConfirm(false, true, grant, Character, Plot(12, 5), Ceiling - 1));
        Assert.False(RealEntryGate.MayConfirm(false, true, grant, Character, Plot(12, 5), Ceiling + 1));
        Assert.False(RealEntryGate.MayConfirm(false, true, grant, Character + 1, Plot(12, 5), Ceiling));
        Assert.False(RealEntryGate.MayConfirm(false, true, grant, Character, Plot(12, 6), Ceiling));
        Assert.False(RealEntryGate.MayConfirm(false, true, grant, Character, Plot(12, 5, OtherWorld), Ceiling));
        Assert.False(RealEntryGate.MayConfirm(false, true, grant, Character, Plot(12, 5), 0));
    }

    [Fact]
    public void UsedGrantOpensNothing()
    {
        var used = Grant() with { Used = true };

        Assert.False(RealEntryGate.MayConfirm(false, true, used, Character, Plot(12, 5), Ceiling));
        Assert.Equal(EntryMode.DryRun, RealEntryGate.ModeFor(false, true, used, Character, [Plot(12, 5)]));
    }

    [Fact]
    public void RunIsRealOnlyWhenTheGrantedPlotIsAmongItsTargets()
    {
        var grant = Grant();

        Assert.Equal(EntryMode.Real, RealEntryGate.ModeFor(false, true, grant, Character, [Plot(3, 1), Plot(12, 5)]));
        Assert.Equal(EntryMode.DryRun, RealEntryGate.ModeFor(false, true, grant, Character, [Plot(3, 1)]));
        Assert.Equal(EntryMode.DryRun, RealEntryGate.ModeFor(false, true, grant, Character + 1, [Plot(12, 5)]));
    }

    [Fact]
    public void ValidatedCapabilityStillHonoursADryRunRequest()
    {
        Assert.False(RealEntryGate.MayConfirm(true, dryRunRequested: true, null, Character, Plot(12, 5), Deposit));
        Assert.True(RealEntryGate.MayConfirm(true, dryRunRequested: false, null, Character, Plot(12, 5), Deposit));
    }

    private static RealEntryGrant Grant() => new(Character, "Test Character", Plot(12, 5), Ceiling, ObservedAt);
}
