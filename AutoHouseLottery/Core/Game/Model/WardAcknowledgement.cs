namespace AutoHouseLottery.Core.Game.Model;

// RowsDue: everything but the rows themselves says the requested ward is shown, so the caller reads the rows next.
// NoPacketObserver: the ward packet is not observed on this build, and the window alone cannot prove which ward its
// rows belong to, so nothing is acknowledged.
internal enum WardAcknowledgement : byte
{
    Pending,
    RowsDue,
    Acknowledged,
    WindowClosed,
    LayoutMismatch,
    ScopeChanged,
    NoPacketObserver,
    TimedOut,
}

// Sequences come from one counter shared by the request, the window updates and the packet, so "after" is exact.
internal readonly record struct WardRequest(int WardIndex, uint TerritoryTypeId, uint WorldId, long Sequence, long DeadlineMs);

// The cheap part of the window: whether it is open, whether its ward number and row count are where the layout says,
// which ward it shows and when it last updated. The rows are read only once this part acknowledges the request.
internal readonly record struct WardWindowHeader(bool Open, bool HeaderValid, int ShownWardIndex, long LastUpdateSequence);

internal readonly record struct WardPacketState(bool ObserverActive, bool Present, int WardIndex, uint TerritoryTypeId, uint WorldId, long Sequence);

internal readonly record struct WardScope(uint TerritoryTypeId, uint WorldId);

internal static class WardAcknowledgementRule
{
    // A window that has not refreshed since the request can still show the previous ward's rows under the same row
    // count, and a refresh can show the new ward number before its rows arrive. Rows count only when this ward's
    // packet arrived after the request and the window refreshed after that packet.
    public static WardAcknowledgement Evaluate(in WardRequest request, in WardWindowHeader window, in WardPacketState packet, in WardScope current, long nowMs)
    {
        if (!window.Open)
        {
            return WardAcknowledgement.WindowClosed;
        }

        if (current.TerritoryTypeId != request.TerritoryTypeId || current.WorldId != request.WorldId)
        {
            return WardAcknowledgement.ScopeChanged;
        }

        if (!packet.ObserverActive)
        {
            return WardAcknowledgement.NoPacketObserver;
        }

        var updatedSinceRequest = window.LastUpdateSequence > request.Sequence;
        if (updatedSinceRequest && !window.HeaderValid)
        {
            return WardAcknowledgement.LayoutMismatch;
        }

        if (updatedSinceRequest
            && window.ShownWardIndex == request.WardIndex
            && packet.Present
            && packet.Sequence > request.Sequence
            && packet.WardIndex == request.WardIndex
            && packet.TerritoryTypeId == request.TerritoryTypeId
            && packet.WorldId == request.WorldId
            && window.LastUpdateSequence > packet.Sequence)
        {
            return WardAcknowledgement.RowsDue;
        }

        return PendingOrTimedOut(request, nowMs);
    }

    // What the rows read decides once the header is right: complete rows acknowledge, rows of the wrong shape stop the
    // reader, and rows not filled in yet keep the request waiting until its deadline.
    public static WardAcknowledgement AfterRows(WardReadStatus rows, in WardRequest request, long nowMs) => rows switch
    {
        WardReadStatus.Read => WardAcknowledgement.Acknowledged,
        WardReadStatus.WindowClosed => WardAcknowledgement.WindowClosed,
        WardReadStatus.LayoutMismatch => WardAcknowledgement.LayoutMismatch,
        _ => PendingOrTimedOut(request, nowMs),
    };

    private static WardAcknowledgement PendingOrTimedOut(in WardRequest request, long nowMs)
        => nowMs >= request.DeadlineMs ? WardAcknowledgement.TimedOut : WardAcknowledgement.Pending;
}
