using AutoHouseLottery.Core.Batch.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Selection.Model;
using AutoHouseLottery.Core.Tasks.Model;

namespace AutoHouseLottery.Tests.Batch;

public sealed class BatchStartRulesTests
{
    private static readonly BatchStartFacts ready = new(
        Running: false,
        OtherWorkBusy: false,
        UnfinishedBatch: false,
        LoggedIn: true,
        HomeRegion: WorldRegion.Europe,
        AutoAnswerLoaded: false,
        InContent: false,
        ShortlistPolicy: true,
        PolicyError: RunPolicyError.None,
        Blockers: RunBlocker.None,
        JournalWritable: true,
        Assignments: 3,
        LoggedInAssigned: true,
        LoggedInPending: false,
        SwitchingAllowed: false);

    [Fact]
    public void AReadyBatchStarts()
    {
        Assert.Equal(StartRefusal.None, BatchStartRules.Check(ready));
        Assert.Equal(StartRefusal.None, BatchStartRules.Check(ready with { Blockers = RunBlocker.CycleNotVerified }));
    }

    [Fact]
    public void WithoutSwitchingABatchStartsOnlyFromOneOfItsOwnCharacters()
    {
        Assert.Equal(StartRefusal.CharacterNotInPolicy, BatchStartRules.Check(ready with { LoggedInAssigned = false }));
        Assert.Equal(StartRefusal.None, BatchStartRules.Check(ready with { LoggedInAssigned = false, SwitchingAllowed = true }));
    }

    [Fact]
    public void ANewBatchNeverReplacesOneThatStillWaits()
    {
        Assert.Equal(StartRefusal.BatchWaiting, BatchStartRules.Check(ready with { UnfinishedBatch = true }));
    }

    [Theory]
    [InlineData((byte)RunBlocker.EntryPeriodClosed, (byte)StartRefusal.EntryPeriodClosed)]
    [InlineData((byte)RunBlocker.CycleExpired, (byte)StartRefusal.EntryPeriodClosed)]
    [InlineData((byte)RunBlocker.MultipleWinsNeedConfirmation, (byte)StartRefusal.MultipleWinsNeedConfirmation)]
    [InlineData((byte)RunBlocker.DepositTotalOverflow, (byte)StartRefusal.InvalidPolicy)]
    [InlineData((byte)RunBlocker.InvalidPolicy, (byte)StartRefusal.InvalidPolicy)]
    public void ThePreviewsBlockersRefuseTheStart(byte blocker, byte refusal)
    {
        Assert.Equal((StartRefusal)refusal, BatchStartRules.Check(ready with { Blockers = (RunBlocker)blocker }));
    }

    [Fact]
    public void TheSingleRunChecksStillApply()
    {
        Assert.Equal(StartRefusal.AlreadyRunning, BatchStartRules.Check(ready with { Running = true }));
        Assert.Equal(StartRefusal.Busy, BatchStartRules.Check(ready with { OtherWorkBusy = true }));
        Assert.Equal(StartRefusal.NotLoggedIn, BatchStartRules.Check(ready with { LoggedIn = false }));
        Assert.Equal(StartRefusal.UnsupportedRegion, BatchStartRules.Check(ready with { HomeRegion = WorldRegion.Korea }));
        Assert.Equal(StartRefusal.UnknownRegion, BatchStartRules.Check(ready with { HomeRegion = WorldRegion.Unknown }));
        Assert.Equal(StartRefusal.AutoAnswerPlugin, BatchStartRules.Check(ready with { AutoAnswerLoaded = true }));
        Assert.Equal(StartRefusal.InContent, BatchStartRules.Check(ready with { InContent = true }));
        Assert.Equal(StartRefusal.WrongMode, BatchStartRules.Check(ready with { ShortlistPolicy = false }));
        Assert.Equal(StartRefusal.InvalidPolicy, BatchStartRules.Check(ready with { PolicyError = RunPolicyError.MissingBuyerType }));
        Assert.Equal(StartRefusal.JournalUnavailable, BatchStartRules.Check(ready with { JournalWritable = false }));
        Assert.Equal(StartRefusal.NoAssignment, BatchStartRules.Check(ready with { Assignments = 0 }));
        Assert.Equal(StartRefusal.ReconciliationPending, BatchStartRules.Check(ready with { LoggedInPending = true }));
    }
}
