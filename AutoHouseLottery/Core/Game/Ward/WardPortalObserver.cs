using AutoHouseLottery.Core.Game.Model;
using Dalamud.Hooking;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Network;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System.Runtime.InteropServices;

namespace AutoHouseLottery.Core.Game.Ward;

// Passively copies each ward packet the portal agent reads (ClientStructs AgentHousingPortal.ReadPacket, the hook point
// AltMate and PaissaHouse use). It never sends anything; the ward selector's own requests produce the packets.
internal sealed unsafe class WardPortalObserver : IDisposable
{
    // The bytes after the 60 entries, which ClientStructs leaves untyped; PaissaHouse's HousingWardInfo reads them as
    // the ward's purchase type and tenant type.
    private const int PurchaseTypeOffset = 0x968;
    private const int TenantTypeOffset = 0x96A;

    private readonly Hook<ReadPacketDelegate>? hook;
    private readonly object gate = new();
    private WardPortalReading? latest;

    public WardPortalObserver()
    {
        var address = AgentHousingPortal.Addresses.ReadPacket.Value;
        if (address == 0)
        {
            RunLog.Warning("Ward packets: AgentHousingPortal.ReadPacket did not resolve on this build; ward reads fall back to the window alone.");
            return;
        }

        try
        {
            hook = Svc.Hook.HookFromAddress<ReadPacketDelegate>(address, Detour);
            hook.Enable();
            RunLog.Debug($"Ward packets: observing AgentHousingPortal.ReadPacket at 0x{address:X}.");
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, "Ward packets: the hook could not be installed; ward reads fall back to the window alone");
            hook?.Dispose();
            hook = null;
        }
    }

    private delegate void ReadPacketDelegate(AgentHousingPortal* agent, HousingPortalPacket* packet);

    public bool Active => hook is { IsEnabled: true };

    public WardPortalReading? Latest
    {
        get
        {
            lock (gate)
            {
                return latest;
            }
        }
    }

    public void Dispose() => hook?.Dispose();

    // The sequence is taken before the original runs, so a window refresh the packet causes always comes after it.
    private void Detour(AgentHousingPortal* agent, HousingPortalPacket* packet)
    {
        var sequence = GameSequence.Next();
        try
        {
            var reading = Capture(packet, sequence);
            lock (gate)
            {
                latest = reading;
            }

            RunLog.Debug($"Ward packet: territory {reading.TerritoryTypeId}, ward {reading.WardIndex + 1}, world {reading.WorldId}, purchase type {reading.PurchaseType}, tenant type {reading.TenantType}.");
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, "Ward packet: the packet could not be copied");
        }

        hook!.Original(agent, packet);
    }

    private static WardPortalReading Capture(HousingPortalPacket* packet, long sequence)
    {
        var source = packet->HouseInfoEntries;
        var entries = new WardPortalEntry[source.Length];
        for (var plotIndex = 0; plotIndex < source.Length; plotIndex++)
        {
            ref readonly var entry = ref source[plotIndex];
            var appeals = entry.HouseAppeals;
            entries[plotIndex] = new WardPortalEntry(plotIndex, entry.HousePrice, (byte)entry.InfoFlags, appeals[0], appeals[1], appeals[2]);
        }

        var raw = (byte*)packet;
        var houseId = packet->HouseId;
        return new WardPortalReading(
            houseId.TerritoryTypeId,
            houseId.WardIndex,
            houseId.WorldId,
            ImmutableCollectionsMarshal.AsImmutableArray(entries),
            raw[PurchaseTypeOffset],
            raw[TenantTypeOffset],
            sequence,
            DateTime.UtcNow);
    }
}
