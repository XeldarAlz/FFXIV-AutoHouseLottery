using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Selection.Model;
using AutoHouseLottery.Core.Tasks.Model;
using static AutoHouseLottery.Tests.HousingFixtures;

namespace AutoHouseLottery.Tests.Run;

public sealed class StartRulesTests
{
    private const ulong Character = 4_000_000_030;

    [Fact]
    public void ReadyExactRunStartsEvenOnAnEstimatedCycle()
    {
        Assert.Equal(StartRefusal.None, StartRules.Check(Facts() with { Blockers = RunBlocker.CycleNotVerified }));
    }

    [Fact]
    public void PendingAttemptBlocksAnyFurtherEntry()
    {
        Assert.Equal(StartRefusal.ReconciliationPending, StartRules.Check(Facts() with { PendingAttempt = true }));
        Assert.Equal(StartRefusal.ReconciliationPending, StartRules.Check(Facts() with { Slot = EntrySlotState.Unresolved }));
    }

    [Fact]
    public void EnteredCharacterIsNotEnteredAgain()
    {
        Assert.Equal(StartRefusal.AlreadyEntered, StartRules.Check(Facts() with { Slot = EntrySlotState.Entered }));
    }

    [Fact]
    public void UnwritableJournalRefusesTheStart()
    {
        Assert.Equal(StartRefusal.JournalUnavailable, StartRules.Check(Facts() with { JournalWritable = false }));
    }

    [Fact]
    public void ModeMustMatchThePolicysTarget()
    {
        Assert.Equal(StartRefusal.WrongMode, StartRules.Check(Facts() with { Mode = RunMode.BestMatch }));
    }

    [Fact]
    public void ClosedEntryPeriodAndMissingAssignmentRefuse()
    {
        Assert.Equal(StartRefusal.EntryPeriodClosed, StartRules.Check(Facts() with { Blockers = RunBlocker.EntryPeriodClosed }));
        Assert.Equal(StartRefusal.NoAssignment, StartRules.Check(Facts() with { CharacterAssigned = false }));
        Assert.Equal(StartRefusal.NoAssignment, StartRules.Check(Facts() with { Blockers = RunBlocker.NoAssignment }));
    }

    [Fact]
    public void ForeignCharacterOrBusyGameRefuses()
    {
        Assert.Equal(StartRefusal.CharacterNotInPolicy, StartRules.Check(Facts() with { CharacterInPolicy = false }));
        Assert.Equal(StartRefusal.Busy, StartRules.Check(Facts() with { OtherWorkBusy = true }));
        Assert.Equal(StartRefusal.AlreadyRunning, StartRules.Check(Facts() with { Running = true }));
        Assert.Equal(StartRefusal.InContent, StartRules.Check(Facts() with { InContent = true }));
        Assert.Equal(StartRefusal.NotLoggedIn, StartRules.Check(Facts() with { LoggedIn = false }));
    }

    private static StartFacts Facts() => new(
        false,
        false,
        true,
        false,
        RunMode.Exact,
        new RunPolicy
        {
            Revision = 1,
            Cycle = VerifiedEntryPhase(),
            Characters = [new CharacterRunSettings(Character, BuyerType.Private, Deposit, 0)],
            TravelAllowance = 0,
            Exact = new ExactTarget(Plot(12, 5), Deposit),
        },
        true,
        true,
        false,
        EntrySlotState.Unknown,
        RunBlocker.None,
        true);
}
