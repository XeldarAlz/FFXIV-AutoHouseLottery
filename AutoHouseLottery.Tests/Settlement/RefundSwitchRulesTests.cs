using AutoHouseLottery.Core.Settlement.Model;

namespace AutoHouseLottery.Tests.Settlement;

// VALIDATION_PLAN section 7 item 10: with switching disabled only the current character is processed; with it enabled no
// unlisted character is ever selected.
public sealed class RefundSwitchRulesTests
{
    private const ulong Current = 1;
    private const ulong Listed = 2;
    private const ulong Unlisted = 3;
    private const ulong ManualRefunds = 4;
    private const ulong NothingDue = 5;

    private static readonly RefundCandidate[] candidates =
    [
        new(Current, true, true, 4),
        new(Unlisted, false, true, 9),
        new(ManualRefunds, true, false, 3),
        new(NothingDue, true, true, 0),
        new(Listed, true, true, 1),
    ];

    private static readonly HashSet<ulong> none = [];

    [Fact]
    public void TheNextListedCharacterWithRefundsDueIsChosen()
    {
        Assert.Equal(Listed, RefundSwitchRules.Choose(true, true, Current, false, candidates, none));
    }

    [Fact]
    public void WithTheOptionOffOrSwitchingNotAllowedOnlyTheCurrentCharacterIsProcessed()
    {
        Assert.Null(RefundSwitchRules.Choose(optionOn: false, switchingAllowed: true, Current, false, candidates, none));
        Assert.Null(RefundSwitchRules.Choose(optionOn: true, switchingAllowed: false, Current, false, candidates, none));
    }

    [Fact]
    public void TheCurrentCharacterFinishesItsOwnWorkFirst()
    {
        Assert.Null(RefundSwitchRules.Choose(true, true, Current, currentHasWork: true, candidates, none));
    }

    [Fact]
    public void AnUnlistedCharacterIsNeverChosenWhateverItHasDue()
    {
        RefundCandidate[] onlyUnlisted = [new(Unlisted, false, true, 9)];

        Assert.Null(RefundSwitchRules.Choose(true, true, Current, false, onlyUnlisted, none));
    }

    [Fact]
    public void EachCharacterIsSwitchedToOnceASession()
    {
        Assert.Null(RefundSwitchRules.Choose(true, true, Current, false, candidates, new HashSet<ulong> { Listed }));
    }

    [Fact]
    public void TheSessionIsLeftOnlyOnTheSchedulersOwnTerms()
    {
        var free = new SchedulerFacts(true, true, false, true, false, false, false, false, false, false, false, false, SchedulerRules.IdleMsRequired, 0, null, 0);

        Assert.True(RefundSwitchRules.SessionFree(free));
        Assert.False(RefundSwitchRules.SessionFree(free with { IdleMs = SchedulerRules.IdleMsRequired - 1 }));
        Assert.False(RefundSwitchRules.SessionFree(free with { HeldAfterAlert = true }));
        Assert.False(RefundSwitchRules.SessionFree(free with { AutoAnswerLoaded = true }));
        Assert.False(RefundSwitchRules.SessionFree(free with { InCombat = true }));
        Assert.False(RefundSwitchRules.SessionFree(free with { Busy = true }));
        Assert.False(RefundSwitchRules.SessionFree(free with { JournalWritable = false }));
    }
}
