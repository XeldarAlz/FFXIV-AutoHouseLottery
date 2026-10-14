using AutoHouseLottery.Core.Ipc;
using AutoHouseLottery.Core.Travel.Model;
using Dalamud.Game.ClientState.Conditions;
using ECommons;
using ECommons.DalamudServices;

namespace AutoHouseLottery.Core.Travel;

// A character switch through Lifestream's ChangeCharacter under a local operation token, the way HousingTravel travels: one
// switch at a time, never while Lifestream is busy, judged by the game's own content ID and home world rather than by
// Lifestream going idle, and an Abort only for work it can still attribute to itself. It passes a name and a world name
// and stores no credentials. Framework thread only.
internal static class CharacterSwitch
{
    private static long lastToken;
    private static Operation? current;

    public static long? CurrentToken => current?.Token;

    public static SwitchStart Begin(ulong contentId, string name, uint homeWorldId, string homeWorldName)
    {
        if (current is not null)
        {
            RunLog.Warning($"Character switch {current.Token} is still running; not starting another.");
            return new SwitchStart(SwitchStartResult.AlreadySwitching, 0);
        }

        var lifestream = LifestreamIpc.Instance;
        if (!lifestream.Capabilities().CanSwitchCharacters)
        {
            RunLog.Warning("Character switch: Lifestream is not installed, or lacks ChangeCharacter or IsBusy.");
            return new SwitchStart(SwitchStartResult.NotInstalled, 0);
        }

        var previous = LoggedIn() ? Svc.PlayerState.ContentId : 0;
        if (previous == contentId)
        {
            return new SwitchStart(SwitchStartResult.AlreadyThatCharacter, 0);
        }

        if (lifestream.IsBusy() != false)
        {
            RunLog.Warning("Character switch: Lifestream is busy with other work (or cannot say); not starting.");
            return new SwitchStart(SwitchStartResult.DependencyBusy, 0);
        }

        var now = Environment.TickCount64;
        var answer = lifestream.ChangeCharacter(name, homeWorldName);
        RunLog.Info($"Character switch: asked Lifestream to log in {name} of {homeWorldName} ({answer?.ToString() ?? "no answer"}).");
        switch (answer)
        {
            case ErrorCode.Success:
                break;
            case ErrorCode.Invalid_world_specified:
                return new SwitchStart(SwitchStartResult.InvalidWorld, 0);
            case ErrorCode.Plugin_is_busy:
                return new SwitchStart(SwitchStartResult.DependencyBusy, 0);
            default:
                return new SwitchStart(SwitchStartResult.Refused, 0);
        }

        var token = ++lastToken;
        current = new Operation(token, new SwitchRequest(contentId, homeWorldId, previous), new TravelOwnership(now), now);
        return new SwitchStart(SwitchStartResult.Started, token);
    }

    public static SwitchState Observe(long token)
    {
        if (current is not { } operation || operation.Token != token)
        {
            return SwitchState.Cancelled;
        }

        var now = Environment.TickCount64;
        var lifestream = LifestreamIpc.Instance;
        var busy = lifestream.IsBusy();
        operation.Ownership.Observe(busy, now);
        var loggedIn = LoggedIn();
        var ready = loggedIn && Svc.Objects.LocalPlayer is not null && !Svc.Condition[ConditionFlag.BetweenAreas] && !Svc.Condition[ConditionFlag.BetweenAreas51];
        operation.SawLogout |= !loggedIn;
        operation.ReadySinceMs = ready ? operation.ReadySinceMs ?? now : null;
        operation.IdleLoggedOutSinceMs = !loggedIn && busy == false ? operation.IdleLoggedOutSinceMs ?? now : null;
        var observation = new SwitchObservation(
            loggedIn,
            ready,
            loggedIn ? Svc.PlayerState.ContentId : 0,
            loggedIn ? Svc.PlayerState.HomeWorld.RowId : 0,
            operation.SawLogout,
            operation.Ownership.Phase,
            now - operation.StartedMs,
            operation.ReadySinceMs is { } readySince ? now - readySince : 0,
            operation.IdleLoggedOutSinceMs is { } idleSince ? now - idleSince : 0);
        var state = CharacterSwitchRules.Evaluate(operation.Request, observation);
        if (state == SwitchState.Switching)
        {
            return state;
        }

        current = null;
        RunLog.Info($"Character switch {token}: {state} (Lifestream {operation.Ownership.Phase}, logged in {loggedIn}, logout seen {operation.SawLogout}, after {observation.ElapsedMs / TimeUnits.MillisecondsPerSecond}s).");
        if (state is SwitchState.TimedOut or SwitchState.LoginFailed && operation.Ownership.MayAbort(busy))
        {
            RunLog.Info($"Character switch {token}: its own Lifestream login still runs; aborting it.");
            lifestream.Abort();
        }

        return state;
    }

    // Stops our own switch. Lifestream's Abort is global, so it is sent only while Lifestream's busy work is provably the
    // login this switch asked for; otherwise the token is dropped and Lifestream is left alone.
    public static bool CancelOwned(long token)
    {
        if (current is not { } operation || operation.Token != token)
        {
            return false;
        }

        current = null;
        var lifestream = LifestreamIpc.Instance;
        var busy = lifestream.IsBusy();
        operation.Ownership.Observe(busy, Environment.TickCount64);
        if (!operation.Ownership.MayAbort(busy))
        {
            RunLog.Info($"Character switch {token}: stopped; Lifestream's current work is not attributable to it ({operation.Ownership.Phase}), so it is not aborted.");
            return false;
        }

        RunLog.Info($"Character switch {token}: stopped; aborting the Lifestream login it started.");
        return lifestream.Abort();
    }

    public static void Release()
    {
        if (current is { } operation)
        {
            CancelOwned(operation.Token);
        }
    }

    private static bool LoggedIn() => Svc.ClientState.IsLoggedIn && Svc.PlayerState.IsLoaded;

    private sealed class Operation(long token, SwitchRequest request, TravelOwnership ownership, long startedMs)
    {
        public long Token { get; } = token;

        public SwitchRequest Request { get; } = request;

        public TravelOwnership Ownership { get; } = ownership;

        public long StartedMs { get; } = startedMs;

        public bool SawLogout { get; set; }

        public long? ReadySinceMs { get; set; }

        public long? IdleLoggedOutSinceMs { get; set; }
    }
}
