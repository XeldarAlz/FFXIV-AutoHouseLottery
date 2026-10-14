using AutoHouseLottery.Core.Game.Character;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Ipc;
using AutoHouseLottery.Core.Travel.Model;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;

namespace AutoHouseLottery.Core.Travel;

// Travel to a plot through Lifestream under a local operation token. One operation at a time; it does not start while
// Lifestream is busy, judges arrival from the game's own housing state, and aborts only work it can attribute to itself.
// Framework thread only.
internal static class HousingTravel
{
    public const int TravelDeadlineMs = 240_000;

    private static long lastToken;
    private static Operation? current;

    public static TravelStart Begin(in HousingDestination destination)
    {
        if (!destination.IsValid || !ResidentialDistricts.TryFindByTerritory(destination.TerritoryTypeId, out var district))
        {
            RunLog.Warning($"Travel: {destination} is not a valid plot; not travelling.");
            return new TravelStart(TravelStartResult.InvalidDestination, 0);
        }

        if (current is not null)
        {
            RunLog.Warning($"Travel: operation {current.Token} is still running; not starting another.");
            return new TravelStart(TravelStartResult.AlreadyTravelling, 0);
        }

        var lifestream = LifestreamIpc.Instance;
        if (!lifestream.Capabilities().CanTravel)
        {
            RunLog.Warning("Travel: Lifestream is not installed, or lacks GoToHousingAddress or IsBusy.");
            return new TravelStart(TravelStartResult.NotInstalled, 0);
        }

        if (lifestream.IsBusy() != false)
        {
            RunLog.Warning("Travel: Lifestream is busy with other work (or cannot say); not starting.");
            return new TravelStart(TravelStartResult.DependencyBusy, 0);
        }

        var now = Environment.TickCount64;
        var quickTravel = lifestream.IsQuickTravelAvailable(destination, district.AetheryteId);
        if (!lifestream.GoToHousingAddress(destination, district.AetheryteId))
        {
            return new TravelStart(TravelStartResult.RequestFailed, 0);
        }

        var token = ++lastToken;
        current = new Operation(token, destination, district.AetheryteId, new TravelOwnership(now), now + TravelDeadlineMs);
        RunLog.Info($"Travel {token}: asked Lifestream for {destination} (quick travel {quickTravel?.ToString() ?? "unknown"}).");
        return new TravelStart(TravelStartResult.Started, token);
    }

    public static TravelState Observe(long token)
    {
        if (current is not { } operation || operation.Token != token)
        {
            return TravelState.Cancelled;
        }

        var now = Environment.TickCount64;
        var lifestream = LifestreamIpc.Instance;
        var busy = lifestream.IsBusy();
        operation.Ownership.Observe(busy, now);
        var verdict = ArrivalCheck.Evaluate(operation.Destination, ObserveArrival());
        var state = TravelProgressRule.Evaluate(operation.Ownership.Phase, verdict, cancelled: false, now >= operation.DeadlineMs);
        if (state == TravelState.Travelling)
        {
            return state;
        }

        // Lifestream's own IsHere reads the same HousingManager values; it is logged as a second opinion, never decides.
        var lifestreamHere = lifestream.IsHere(operation.Destination, operation.AetheryteId);
        RunLog.Info($"Travel {token}: {state} (Lifestream {operation.Ownership.Phase}, arrival {verdict}, Lifestream IsHere {lifestreamHere?.ToString() ?? "unknown"}).");
        current = null;
        if (state == TravelState.TimedOut && operation.Ownership.MayAbort(busy))
        {
            RunLog.Info($"Travel {token}: timed out while its own Lifestream route still runs; aborting it.");
            lifestream.Abort();
        }

        return state;
    }

    // Stops our own travel. Lifestream's Abort is global, so it is sent only while Lifestream's busy work is provably
    // the work this operation started; otherwise the token is dropped and Lifestream is left alone.
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
            RunLog.Info($"Travel {token}: stopped; Lifestream's current work is not attributable to it ({operation.Ownership.Phase}), so it is not aborted.");
            return false;
        }

        RunLog.Info($"Travel {token}: stopped; aborting the Lifestream travel it started.");
        return lifestream.Abort();
    }

    public static long? CurrentToken => current?.Token;

    public static void Release()
    {
        if (current is { } operation)
        {
            CancelOwned(operation.Token);
        }
    }

    public static ArrivalObservation ObserveArrival()
    {
        var zoneReady = Svc.Objects.LocalPlayer is not null
            && !Svc.Condition[ConditionFlag.BetweenAreas]
            && !Svc.Condition[ConditionFlag.BetweenAreas51];
        return new ArrivalObservation(zoneReady, Svc.PlayerState.CurrentWorld.RowId, CharacterContextReader.ReadLocation());
    }

    private sealed record Operation(long Token, HousingDestination Destination, uint AetheryteId, TravelOwnership Ownership, long DeadlineMs);
}
