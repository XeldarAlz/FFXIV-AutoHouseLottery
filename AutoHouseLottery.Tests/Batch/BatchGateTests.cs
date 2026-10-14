using System.Collections.Immutable;
using AutoHouseLottery.Core.Batch.Model;
using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Selection.Model;
using AutoHouseLottery.Core.Tasks.Model;
using AutoHouseLottery.Core.Travel.Model;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests.Batch;

// Batching must not widen a grant or bypass a flag: each character goes through the single-character gate with its own
// assignment, and switching needs its own flag or a session grant.
public sealed class BatchGateTests
{
    private const ulong Granted = 1;
    private const ulong Other = 2;

    private static readonly PlotKey grantedPlot = Plot(3, 7);
    private static readonly RealEntryGrant grant = new(Granted, "Granted Character", grantedPlot, Deposit, ObservedAt);

    [Fact]
    public void NoCapabilityFlagIsValidatedYet()
    {
        Assert.False(EntryCapabilities.EntryConfirmValidated);
        Assert.False(SwitchCapabilities.CharacterSwitchValidated);
    }

    [Fact]
    public void ARequestedDryRunStaysDryForEveryMemberWhateverIsGranted()
    {
        Assert.Equal(EntryMode.DryRun, BatchGate.EntryModeFor(confirmValidated: true, dryRunRequested: true, grant, Exact(Granted, grantedPlot)));
        Assert.Equal(EntryMode.DryRun, BatchGate.EntryModeFor(false, true, grant, Exact(Granted, grantedPlot)));
    }

    [Fact]
    public void OnlyTheGrantedCharactersOwnExactTargetOnTheGrantedPlotCanBeReal()
    {
        Assert.Equal(EntryMode.Real, BatchGate.EntryModeFor(false, false, grant, Exact(Granted, grantedPlot)));
        Assert.Equal(EntryMode.DryRun, BatchGate.EntryModeFor(false, false, grant, Exact(Other, grantedPlot)));
        Assert.Equal(EntryMode.DryRun, BatchGate.EntryModeFor(false, false, grant, Exact(Granted, Plot(3, 8))));
        Assert.Equal(EntryMode.DryRun, BatchGate.EntryModeFor(false, false, null, Exact(Granted, grantedPlot)));
        Assert.Equal(EntryMode.DryRun, BatchGate.EntryModeFor(false, false, grant with { Used = true }, Exact(Granted, grantedPlot)));
    }

    [Fact]
    public void ABestMatchOrAPlotPinnedByAnotherMemberIsNeverGranted()
    {
        var bestMatch = Assignment(Granted, [grantedPlot, Plot(3, 8)], exact: false);
        var pinned = Assignment(Granted, [grantedPlot], exact: false);

        Assert.Equal(RunMode.BestMatch, BatchGate.ModeOf(bestMatch));
        Assert.Equal(EntryMode.DryRun, BatchGate.EntryModeFor(false, false, grant, bestMatch));
        Assert.Equal(EntryMode.DryRun, BatchGate.EntryModeFor(false, false, grant, pinned));
        Assert.False(RealEntryGate.MayConfirm(false, false, grant, BatchGate.ModeOf(pinned), Granted, grantedPlot, Deposit));
    }

    [Fact]
    public void ConfirmIsCheckedAgainWithTheMembersOwnCharacterPlotAndAmount()
    {
        var mode = BatchGate.ModeOf(Exact(Granted, grantedPlot));

        Assert.True(RealEntryGate.MayConfirm(false, false, grant, mode, Granted, grantedPlot, Deposit));
        Assert.False(RealEntryGate.MayConfirm(false, false, grant, mode, Other, grantedPlot, Deposit));
        Assert.False(RealEntryGate.MayConfirm(false, false, grant, mode, Granted, grantedPlot, Deposit + 1));
    }

    [Fact]
    public void SwitchingNeedsTheFlagOrASessionGrantAndLifestreamsChangeCharacter()
    {
        var switching = new SwitchGrant(ObservedAt);

        Assert.False(SwitchGate.Allowed(SwitchCapabilities.CharacterSwitchValidated, null, dependencyCanSwitch: true));
        Assert.True(SwitchGate.Allowed(SwitchCapabilities.CharacterSwitchValidated, switching, dependencyCanSwitch: true));
        Assert.True(SwitchGate.Allowed(validated: true, null, dependencyCanSwitch: true));
        Assert.False(SwitchGate.Allowed(validated: true, switching, dependencyCanSwitch: false));
    }

    [Fact]
    public void LifestreamCanSwitchOnlyWithChangeCharacterAndItsBusyFlag()
    {
        var complete = new LifestreamCapabilities(true, true, true, true, true, true, true, true) { ChangeCharacter = true };

        Assert.True(complete.CanSwitchCharacters);
        Assert.False((complete with { ChangeCharacter = false }).CanSwitchCharacters);
        Assert.False((complete with { IsBusy = false }).CanSwitchCharacters);
        Assert.False((complete with { Installed = false }).CanSwitchCharacters);
    }

    private static RunAssignment Exact(ulong contentId, PlotKey plot) => Assignment(contentId, [plot], exact: true);

    private static RunAssignment Assignment(ulong contentId, ImmutableArray<PlotKey> targets, bool exact)
        => new(contentId, default, PlacementMode.SamePlot, targets, Deposit, false, new EligibilityResult(EligibilityReason.None, EligibilityReason.None, ObservedAt)) { Exact = exact };
}
