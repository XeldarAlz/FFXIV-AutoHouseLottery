using AutoHouseLottery.Core.Batch.Model;
using AutoHouseLottery.Core.Travel.Model;

namespace AutoHouseLottery.Tests.Batch;

public sealed class BatchSwitchTests
{
    [Theory]
    [InlineData((byte)SwitchStartResult.OtherWork, (byte)BatchHalt.SwitchBlocked)]
    [InlineData((byte)SwitchStartResult.DependencyBusy, (byte)BatchHalt.SwitchBlocked)]
    [InlineData((byte)SwitchStartResult.AlreadySwitching, (byte)BatchHalt.SwitchBlocked)]
    [InlineData((byte)SwitchStartResult.NotInstalled, (byte)BatchHalt.SwitchFailed)]
    [InlineData((byte)SwitchStartResult.AlreadyThatCharacter, (byte)BatchHalt.SwitchFailed)]
    [InlineData((byte)SwitchStartResult.InvalidWorld, (byte)BatchHalt.SwitchFailed)]
    [InlineData((byte)SwitchStartResult.Refused, (byte)BatchHalt.SwitchFailed)]
    public void ASwitchThatCouldNotStartForBusyHelpersIsOnlyBlocked(byte result, byte halt)
    {
        Assert.Equal((BatchHalt)halt, BatchRules.SwitchNotStarted((SwitchStartResult)result));
    }

    [Fact]
    public void NoSwitchCutsShortAQueueAPathOrMultiMode()
    {
        Assert.Equal(SwitchBlock.None, SwitchSafety.Check(new SwitchSafetyFacts(false, false, false)));
        Assert.Equal(SwitchBlock.Queue, SwitchSafety.Check(new SwitchSafetyFacts(true, false, false)));
        Assert.Equal(SwitchBlock.Navigation, SwitchSafety.Check(new SwitchSafetyFacts(false, true, false)));
        Assert.Equal(SwitchBlock.Navigation, SwitchSafety.Check(new SwitchSafetyFacts(false, null, false)));
        Assert.Equal(SwitchBlock.MultiMode, SwitchSafety.Check(new SwitchSafetyFacts(false, false, true)));
        Assert.Equal(SwitchBlock.MultiMode, SwitchSafety.Check(new SwitchSafetyFacts(false, false, null)));
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
}
