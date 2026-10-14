using AutoHouseLottery.Core.Travel.Model;

namespace AutoHouseLottery.Tests.Travel;

// VALIDATION_PLAN section 8, character switching: the previous character's state never stands in for the next one.
public sealed class CharacterSwitchRulesTests
{
    private const ulong Previous = 10;
    private const ulong Requested = 20;
    private const uint HomeWorld = 63;
    private const uint OtherHomeWorld = 79;

    private static readonly SwitchRequest request = new(Requested, HomeWorld, Previous);

    private static readonly SwitchObservation loggedIn = new(
        LoggedIn: true,
        Ready: true,
        ContentId: Requested,
        HomeWorldId: HomeWorld,
        SawLogout: true,
        Dependency: TravelPhase.Finished,
        ElapsedMs: 90_000,
        ReadyForMs: CharacterSwitchRules.SettleMs,
        IdleLoggedOutMs: 0);

    [Fact]
    public void TheRequestedCharacterOnItsHomeWorldCountsOnceItHasSettled()
    {
        Assert.Equal(SwitchState.Switched, CharacterSwitchRules.Evaluate(request, loggedIn));
        Assert.Equal(SwitchState.Switching, CharacterSwitchRules.Evaluate(request, loggedIn with { ReadyForMs = CharacterSwitchRules.SettleMs - 1 }));
        Assert.Equal(SwitchState.Switching, CharacterSwitchRules.Evaluate(request, loggedIn with { Ready = false }));
    }

    [Fact]
    public void ThePlayerChoosingAnotherCharacterIsAWrongCharacter()
    {
        Assert.Equal(SwitchState.WrongCharacter, CharacterSwitchRules.Evaluate(request, loggedIn with { ContentId = 30 }));
        Assert.Equal(SwitchState.WrongCharacter, CharacterSwitchRules.Evaluate(request, loggedIn with { ContentId = Previous }));
    }

    [Fact]
    public void TheRightContentIdOnAnotherHomeWorldDoesNotCount()
    {
        Assert.Equal(SwitchState.WrongHomeWorld, CharacterSwitchRules.Evaluate(request, loggedIn with { HomeWorldId = OtherHomeWorld }));
    }

    [Fact]
    public void ThePreviousCharacterIsNeverTakenForTheSwitchBeforeItLoggedOut()
    {
        var stillThere = loggedIn with { ContentId = Previous, SawLogout = false, Dependency = TravelPhase.Running, ElapsedMs = 2_000 };

        Assert.Equal(SwitchState.Switching, CharacterSwitchRules.Evaluate(request, stillThere));
        Assert.Equal(SwitchState.NotStarted, CharacterSwitchRules.Evaluate(request, stillThere with { Dependency = TravelPhase.Finished }));
        Assert.Equal(SwitchState.NotStarted, CharacterSwitchRules.Evaluate(request, stillThere with { Dependency = TravelPhase.NeverStarted }));
    }

    [Fact]
    public void ALoginThatNeverComesIsAFailedLogin()
    {
        var titleScreen = new SwitchObservation(false, false, 0, 0, true, TravelPhase.Finished, 120_000, 0, CharacterSwitchRules.LoginFailedGraceMs);

        Assert.Equal(SwitchState.LoginFailed, CharacterSwitchRules.Evaluate(request, titleScreen));
        Assert.Equal(SwitchState.Switching, CharacterSwitchRules.Evaluate(request, titleScreen with { IdleLoggedOutMs = CharacterSwitchRules.LoginFailedGraceMs - 1 }));
    }

    [Fact]
    public void TheFiveMinuteDeadlineEndsAnyUnfinishedSwitch()
    {
        var loading = new SwitchObservation(false, false, 0, 0, true, TravelPhase.Running, CharacterSwitchRules.DeadlineMs, 0, 0);

        Assert.Equal(SwitchState.TimedOut, CharacterSwitchRules.Evaluate(request, loading));
        Assert.Equal(SwitchState.Switching, CharacterSwitchRules.Evaluate(request, loading with { ElapsedMs = CharacterSwitchRules.DeadlineMs - 1 }));
        Assert.Equal(300_000, CharacterSwitchRules.DeadlineMs);
    }

    [Fact]
    public void OnlySwitchingIsNotTerminal()
    {
        Assert.False(CharacterSwitchRules.IsTerminal(SwitchState.Switching));
        Assert.True(CharacterSwitchRules.IsTerminal(SwitchState.Cancelled));
    }
}
