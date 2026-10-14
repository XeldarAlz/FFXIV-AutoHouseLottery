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

internal enum SwitchScope : byte { Batch, RefundQueue }

// A maintainer's permission to switch characters, kept in memory only. Switching logs out by design, so it outlives a
// logout. A batch grant serves the next batch started and that batch only (BatchId, bound when it starts); a refund grant
// serves the refund queue. Each ends after two hours, when revoked, or at unload.
internal sealed record SwitchGrant(SwitchScope Scope, DateTime IssuedUtc)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(2);

    public Guid? BatchId { get; init; }

    public bool Expired(DateTime nowUtc) => nowUtc - IssuedUtc >= Lifetime;
}

internal static class SwitchGate
{
    // BatchId is the batch asking, or null for a batch not started yet; a grant already bound to another batch is not its.
    public static bool AllowsBatch(bool validated, SwitchGrant? grant, Guid? batchId, bool dependencyCanSwitch, DateTime nowUtc)
        => dependencyCanSwitch
        && (validated || (grant is { Scope: SwitchScope.Batch } batchGrant && !batchGrant.Expired(nowUtc) && (batchGrant.BatchId is null || batchGrant.BatchId == batchId)));

    public static bool AllowsRefundQueue(bool validated, SwitchGrant? grant, bool dependencyCanSwitch, DateTime nowUtc)
        => dependencyCanSwitch && (validated || (grant is { Scope: SwitchScope.RefundQueue } refundGrant && !refundGrant.Expired(nowUtc)));
}

// What else may be using the game right before a switch would log out. Null: the helper is loaded but could not answer,
// which counts as busy; a helper that is not loaded is never busy.
internal readonly record struct SwitchSafetyFacts(bool InQueue, bool? NavigationRunning, bool? MultiModeEnabled);

internal enum SwitchBlock : byte { None, Queue, Navigation, MultiMode }

internal static class SwitchSafety
{
    // A logout must never cut short a duty, match or world visit queue, a vnavmesh path or AutoRetainer's multi-mode.
    public static SwitchBlock Check(in SwitchSafetyFacts facts)
    {
        if (facts.InQueue)
        {
            return SwitchBlock.Queue;
        }

        if (facts.NavigationRunning != false)
        {
            return SwitchBlock.Navigation;
        }

        return facts.MultiModeEnabled != false ? SwitchBlock.MultiMode : SwitchBlock.None;
    }
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
    OtherWork,
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

internal readonly record struct SwitchRequest(ulong ContentId, uint HomeWorldId);

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
    // The requested content ID and home world must both match, and Lifestream must have finished its own work, before the
    // switch is done. Lifestream busy again with work that is not ours means the switch is not ours to finish either.
    public static SwitchState Evaluate(in SwitchRequest request, in SwitchObservation observation)
    {
        if (observation.Dependency == TravelPhase.TakenOver)
        {
            return SwitchState.NotStarted;
        }

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

            if (observation.ReadyForMs >= SettleMs && observation.Dependency is TravelPhase.Finished or TravelPhase.NeverStarted)
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
}
