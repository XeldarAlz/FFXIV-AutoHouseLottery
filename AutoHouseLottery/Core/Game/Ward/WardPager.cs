using AutoHouseLottery.Core.Game.Model;
using ECommons.Automation;
using ECommons.DalamudServices;

namespace AutoHouseLottery.Core.Game.Ward;

internal static unsafe class WardPager
{
    public const int AcknowledgeTimeoutMs = 8_000;

    // HouseHunter's scan loop and LlamaLibrary's HousingSelectBlock.SelectWard both select a ward with (1, wardIndex).
    private const int SelectWardCallback = 1;
    private const long NotRequested = 0;

    public static WardRequest? Request(int wardIndex)
    {
        if (!ResidentialDistricts.IsWardIndex(wardIndex) || !AddonReads.TryGetReady(WardReader.AddonName, out var addon))
        {
            RunLog.Warning($"Ward pager: ward {wardIndex + 1} not requested; the ward selector is not open or the ward is out of range.");
            return null;
        }

        if (WardReader.PortalTerritory() is not { } territoryTypeId)
        {
            RunLog.Warning("Ward pager: the housing portal agent is not available, so the district of the ward cannot be checked.");
            return null;
        }

        var request = new WardRequest(wardIndex, territoryTypeId, Svc.PlayerState.CurrentWorld.RowId, GameSequence.Next(), Environment.TickCount64 + AcknowledgeTimeoutMs);
        Callback.Fire(addon, true, SelectWardCallback, wardIndex);
        RunLog.Debug($"Ward pager: requested ward {wardIndex + 1} of territory {territoryTypeId} (sequence {request.Sequence}).");
        return request;
    }

    // The ward the selector already shows needs no request, provided the window updated after that ward's packet since
    // the plugin started watching. Selecting the shown ward again may not make the game send anything.
    public static WardReading? AcceptDisplayed(int wardIndex, AddonUpdateCounter window, WardPortalObserver packets)
    {
        var sinceWatching = new WardRequest(wardIndex, WardReader.PortalTerritory() ?? 0, Svc.PlayerState.CurrentWorld.RowId, NotRequested, Environment.TickCount64);
        return Poll(sinceWatching, window, packets, out var reading) == WardAcknowledgement.Acknowledged ? reading : null;
    }

    public static WardAcknowledgement Poll(in WardRequest request, AddonUpdateCounter window, WardPortalObserver packets, out WardReading? reading)
    {
        reading = null;
        var latest = packets.Latest;
        var packetState = latest is null
            ? new WardPacketState(packets.Active, false, WardReader.NoWard, 0, 0, 0)
            : new WardPacketState(packets.Active, true, latest.WardIndex, latest.TerritoryTypeId, latest.WorldId, latest.Sequence);
        var scope = new WardScope(WardReader.PortalTerritory() ?? 0, Svc.PlayerState.CurrentWorld.RowId);
        var now = Environment.TickCount64;
        var acknowledgement = WardAcknowledgementRule.Evaluate(request, WardReader.ReadHeader(window.LastUpdateSequence), packetState, scope, now);
        if (acknowledgement != WardAcknowledgement.RowsDue)
        {
            return acknowledgement;
        }

        acknowledgement = WardAcknowledgementRule.AfterRows(WardReader.ReadDisplayed(window.LastUpdateSequence, out var displayed), request, now);
        if (acknowledgement == WardAcknowledgement.Acknowledged)
        {
            reading = displayed!.Acknowledged(latest!);
        }

        return acknowledgement;
    }
}
