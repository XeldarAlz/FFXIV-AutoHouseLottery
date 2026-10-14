namespace AutoHouseLottery.Core.Travel.Model;

internal static class SwitchCapabilities
{
    // True only once supervised batch and refund runs have shown, on the supported build, that Lifestream's ChangeCharacter
    // logs out and logs in exactly the requested character, that a failed login, a cancel and a plugin reload on the title
    // screen end where these rules say, and that nothing of the previous character's state leaks into the next one. Until
    // then only a maintainer's session grant lets a batch or the refund queue switch characters; otherwise each character
    // is processed at its own normal login.
    public const bool CharacterSwitchValidated = false;
}

// A maintainer's permission to switch characters, kept in memory only. Switching logs out by design, so it outlives a
// logout and ends at unload or when revoked.
internal sealed record SwitchGrant(DateTime IssuedUtc);

internal static class SwitchGate
{
    public static bool Allowed(bool validated, SwitchGrant? grant, bool dependencyCanSwitch) => dependencyCanSwitch && (validated || grant is not null);
}

internal enum SwitchStartResult : byte
{
    Started,
    NotInstalled,
    DependencyBusy,
    AlreadySwitching,
    AlreadyThatCharacter,
    InvalidWorld,
    Refused,
}

internal readonly record struct SwitchStart(SwitchStartResult Result, long Token);

internal enum SwitchState : byte
{
    Switching,
    Switched,
    WrongCharacter,
    WrongHomeWorld,
    NotStarted,
    LoginFailed,
    TimedOut,
    Cancelled,
}

// One sample of the switch, taken on the framework thread. LoggedIn and Ready: a character is loaded and its zone is
// usable. SawLogout: the previous character has been logged out since the request. Dependency: Lifestream's work as
// seen through its busy flag. ReadyForMs: how long the logged-in character has been ready without a break.
// IdleLoggedOutMs: how long nobody has been logged in while Lifestream had nothing running.
internal readonly record struct SwitchObservation(
    bool LoggedIn,
    bool Ready,
    ulong ContentId,
    uint HomeWorldId,
    bool SawLogout,
    TravelPhase Dependency,
    long ElapsedMs,
    long ReadyForMs,
    long IdleLoggedOutMs);

internal readonly record struct SwitchRequest(ulong ContentId, uint HomeWorldId, ulong PreviousContentId);

internal static class CharacterSwitchRules
{
    // TECHNICAL_DESIGN section 12: character switching has its own five-minute deadline.
    public const int DeadlineMs = 300_000;

    // A freshly loaded character's Free Company, estates and gil arrive over the first moments; nothing acts before then.
    public const int SettleMs = 5_000;

    // Loading into the world after the character screen can leave Lifestream idle for a while, so a login is called failed
    // only after a long stretch with nobody logged in and nothing running.
    public const int LoginFailedGraceMs = 60_000;

    // Only a character that logged in after the previous one logged out counts; the previous one is never taken for it.
    // The requested content ID and home world must both match before the switch is done.
    public static SwitchState Evaluate(in SwitchRequest request, in SwitchObservation observation)
    {
        if (observation.LoggedIn && observation.Ready && observation.SawLogout)
        {
            if (observation.ContentId != request.ContentId)
            {
                return SwitchState.WrongCharacter;
            }

            if (observation.HomeWorldId != request.HomeWorldId)
            {
                return SwitchState.WrongHomeWorld;
            }

            if (observation.ReadyForMs >= SettleMs)
            {
                return SwitchState.Switched;
            }
        }
        else if (observation.LoggedIn && !observation.SawLogout && observation.Dependency is TravelPhase.NeverStarted or TravelPhase.Finished)
        {
            return SwitchState.NotStarted;
        }
        else if (!observation.LoggedIn && observation.IdleLoggedOutMs >= LoginFailedGraceMs)
        {
            return SwitchState.LoginFailed;
        }

        return observation.ElapsedMs >= DeadlineMs ? SwitchState.TimedOut : SwitchState.Switching;
    }

    public static bool IsTerminal(SwitchState state) => state != SwitchState.Switching;
}
