using AutoHouseLottery.Core.Batch.Model;
using AutoHouseLottery.Core.Tasks.Model;
using AutoHouseLottery.Core.Travel.Model;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests.Batch;

// Batching must not widen a grant or get past a flag: a batch goes real only with both the entry and the batch
// validated, a one-plot grant never makes a member real, and switching needs its own flag or a scoped, expiring grant.
public sealed class BatchGateTests
{
    private static readonly DateTime issued = ObservedAt;
    private static readonly Guid batchId = Guid.NewGuid();

    [Fact]
    public void NoCapabilityFlagIsValidatedYet()
    {
        Assert.False(EntryCapabilities.EntryConfirmValidated);
        Assert.False(BatchCapabilities.BatchEntryValidated);
        Assert.False(SwitchCapabilities.CharacterSwitchValidated);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public void ABatchGoesRealOnlyWithTheEntryAndTheBatchBothValidated(bool confirmValidated, bool batchValidated, bool real)
    {
        Assert.Equal(real ? EntryMode.Real : EntryMode.DryRun, BatchGate.EntryModeFor(confirmValidated, batchValidated, dryRunRequested: false));
        Assert.Equal(!real, BatchGate.MemberDryRun(confirmValidated, batchValidated, dryRunRequested: false));
        Assert.Equal(EntryMode.DryRun, BatchGate.EntryModeFor(confirmValidated, batchValidated, dryRunRequested: true));
    }

    [Fact]
    public void AOnePlotGrantNeverMakesAMemberReal()
    {
        var plot = Plot(3, 7);
        var grant = new RealEntryGrant(1, "Granted Character", plot, Deposit, ObservedAt);
        var memberDryRun = BatchGate.MemberDryRun(EntryCapabilities.EntryConfirmValidated, BatchCapabilities.BatchEntryValidated, dryRunRequested: false);

        Assert.True(memberDryRun);
        Assert.False(RealEntryGate.MayConfirm(EntryCapabilities.EntryConfirmValidated, memberDryRun, grant, RunMode.Exact, 1, plot, Deposit));
        Assert.Equal(EntryMode.DryRun, RealEntryGate.ModeFor(EntryCapabilities.EntryConfirmValidated, memberDryRun, grant, RunMode.Exact, 1, [plot]));
    }

    [Fact]
    public void ABatchGrantServesOneBatchOnlyAndEndsAfterTwoHours()
    {
        var unbound = new SwitchGrant(SwitchScope.Batch, issued);
        var bound = unbound with { BatchId = batchId };

        Assert.True(SwitchGate.AllowsBatch(false, unbound, null, dependencyCanSwitch: true, issued));
        Assert.True(SwitchGate.AllowsBatch(false, unbound, batchId, true, issued));
        Assert.True(SwitchGate.AllowsBatch(false, bound, batchId, true, issued));
        Assert.False(SwitchGate.AllowsBatch(false, bound, Guid.NewGuid(), true, issued));
        Assert.False(SwitchGate.AllowsBatch(false, bound, null, true, issued));
        Assert.False(SwitchGate.AllowsBatch(false, bound, batchId, true, issued + SwitchGrant.Lifetime));
        Assert.False(SwitchGate.AllowsBatch(false, null, batchId, true, issued));
        Assert.True(SwitchGate.AllowsBatch(validated: true, null, batchId, true, issued));
        Assert.False(SwitchGate.AllowsBatch(validated: true, bound, batchId, dependencyCanSwitch: false, issued));
    }

    [Fact]
    public void AGrantIssuedWhileABatchIsUnfinishedServesThatBatchAndNoOther()
    {
        var unfinished = SwitchGrant.ForBatch(batchId, issued);
        var reissued = SwitchGrant.ForBatch(batchId, issued + TimeSpan.FromMinutes(5));
        var forTheNext = SwitchGrant.ForBatch(null, issued);

        Assert.Equal(batchId, unfinished.BatchId);
        Assert.Equal(batchId, reissued.BatchId);
        Assert.True(SwitchGate.AllowsBatch(false, unfinished, batchId, true, issued));
        Assert.False(SwitchGate.AllowsBatch(false, unfinished, Guid.NewGuid(), true, issued));
        Assert.False(SwitchGate.AllowsBatch(false, unfinished, null, true, issued));
        Assert.Equal(batchId, unfinished.BoundTo(Guid.NewGuid()).BatchId);
        Assert.Null(forTheNext.BatchId);
        Assert.Equal(batchId, forTheNext.BoundTo(batchId).BatchId);
    }

    [Theory]
    [InlineData((byte)SwitchStartResult.OtherWork, (byte)BatchHalt.SwitchBlocked)]
    [InlineData((byte)SwitchStartResult.DependencyBusy, (byte)BatchHalt.SwitchBlocked)]
    [InlineData((byte)SwitchStartResult.AlreadySwitching, (byte)BatchHalt.SwitchBlocked)]
    [InlineData((byte)SwitchStartResult.NotInstalled, (byte)BatchHalt.SwitchFailed)]
    [InlineData((byte)SwitchStartResult.AlreadyThatCharacter, (byte)BatchHalt.SwitchFailed)]
    [InlineData((byte)SwitchStartResult.InvalidWorld, (byte)BatchHalt.SwitchFailed)]
    [InlineData((byte)SwitchStartResult.Refused, (byte)BatchHalt.SwitchFailed)]
    public void ASwitchThatCouldNotStartForBusyHelpersIsOnlyBlockedAndKeepsTheGrant(byte result, byte halt)
    {
        var running = BatchRulesTests.Batch(out _);
        var expected = (BatchHalt)halt;

        Assert.Equal(expected, BatchRules.SwitchNotStarted((SwitchStartResult)result));
        Assert.Equal(expected == BatchHalt.SwitchFailed, BatchRules.EndsSwitchGrant(BatchRules.Halt(running, expected, issued)));
    }

    [Fact]
    public void EachGrantCoversOnlyItsOwnScope()
    {
        var batchGrant = new SwitchGrant(SwitchScope.Batch, issued);
        var refundGrant = new SwitchGrant(SwitchScope.RefundQueue, issued);

        Assert.False(SwitchGate.AllowsRefundQueue(false, batchGrant, true, issued));
        Assert.False(SwitchGate.AllowsBatch(false, refundGrant, batchId, true, issued));
        Assert.True(SwitchGate.AllowsRefundQueue(false, refundGrant, true, issued));
        Assert.False(SwitchGate.AllowsRefundQueue(false, refundGrant, true, issued + SwitchGrant.Lifetime));
        Assert.False(SwitchGate.AllowsRefundQueue(false, refundGrant, dependencyCanSwitch: false, issued));
    }

    [Fact]
    public void ABatchGrantEndsWhenItsBatchFinishesIsDroppedOrFailsASwitch()
    {
        var running = BatchRulesTests.Batch(out _);

        Assert.True(BatchRules.EndsSwitchGrant(null));
        Assert.False(BatchRules.EndsSwitchGrant(running));
        Assert.True(BatchRules.EndsSwitchGrant(BatchRules.Finish(running, BatchHalt.None, issued)));
        Assert.True(BatchRules.EndsSwitchGrant(BatchRules.Halt(running, BatchHalt.WrongCharacter, issued)));
        Assert.True(BatchRules.EndsSwitchGrant(BatchRules.Halt(running, BatchHalt.SwitchFailed, issued)));
        Assert.False(BatchRules.EndsSwitchGrant(BatchRules.Halt(running, BatchHalt.StoppedByPlayer, issued)));
        Assert.False(BatchRules.EndsSwitchGrant(BatchRules.Halt(running, BatchHalt.SwitchBlocked, issued)));
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
