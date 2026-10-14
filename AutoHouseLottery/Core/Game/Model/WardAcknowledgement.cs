namespace AutoHouseLottery.Core.Game.Model;

internal enum WardAcknowledgement : byte
{
    Pending,
    Acknowledged,
    WindowClosed,
    LayoutMismatch,
    ScopeChanged,
    TimedOut,
}

// Sequences come from one counter shared by the request, the window updates and the packet, so "after" is exact.
internal readonly record struct WardRequest(int WardIndex, uint TerritoryTypeId, uint WorldId, long Sequence, long DeadlineMs);

internal readonly record struct WardWindowState(bool Open, bool LayoutValid, bool RowsComplete, int ShownWardIndex, long LastUpdateSequence);

internal readonly record struct WardPacketState(bool ObserverActive, bool Present, int WardIndex, uint TerritoryTypeId, uint WorldId, long Sequence);

internal readonly record struct WardScope(uint TerritoryTypeId, uint WorldId);

internal static class WardAcknowledgementRule
{
    // A window that has not refreshed since the request can still show the previous ward's rows under the same row
    // count, and a refresh can show the new ward number before its rows arrive. Rows count only once the window has
    // refreshed after the request and shows the requested ward, and, when the packet is observed, after that ward's
    // packet as well.
    public static WardAcknowledgement Evaluate(in WardRequest request, in WardWindowState window, in WardPacketState packet, in WardScope current, long nowMs)
    {
        if (!window.Open)
        {
            return WardAcknowledgement.WindowClosed;
        }

        if (current.TerritoryTypeId != request.TerritoryTypeId || current.WorldId != request.WorldId)
        {
            return WardAcknowledgement.ScopeChanged;
        }

        if (IsAcknowledged(request, window, packet))
        {
            return WardAcknowledgement.Acknowledged;
        }

        if (window.LastUpdateSequence > request.Sequence && !window.LayoutValid)
        {
            return WardAcknowledgement.LayoutMismatch;
        }

        return nowMs >= request.DeadlineMs ? WardAcknowledgement.TimedOut : WardAcknowledgement.Pending;
    }

    public static WardCorrelation CorrelationOf(in WardPacketState packet)
        => packet.ObserverActive ? WardCorrelation.PacketAndAddon : WardCorrelation.AddonOnly;

    private static bool IsAcknowledged(in WardRequest request, in WardWindowState window, in WardPacketState packet)
    {
        if (window.LastUpdateSequence <= request.Sequence || !window.LayoutValid || !window.RowsComplete || window.ShownWardIndex != request.WardIndex)
        {
            return false;
        }

        if (!packet.ObserverActive)
        {
            return true;
        }

        return packet.Present
            && packet.Sequence > request.Sequence
            && packet.WardIndex == request.WardIndex
            && packet.TerritoryTypeId == request.TerritoryTypeId
            && packet.WorldId == request.WorldId
            && window.LastUpdateSequence > packet.Sequence;
    }
}
