using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Game.Model;

internal enum WardScanStop : byte
{
    Completed,
    Cancelled,
    RequestRefused,
    NoPacketObserver,
    WindowClosed,
    LayoutMismatch,
    ScopeChanged,
    TimedOut,
}

// Wards holds only acknowledged wards, in order; a scan that stopped early covers Wards and nothing after StoppedAtWard.
internal sealed record WardScanResult(ImmutableArray<WardReading> Wards, int WardsPlanned, WardScanStop Stop, int? StoppedAtWard)
{
    public bool Complete => Stop == WardScanStop.Completed && Wards.Length == WardsPlanned;

    public static WardScanStop StopFor(WardAcknowledgement acknowledgement) => acknowledgement switch
    {
        WardAcknowledgement.WindowClosed => WardScanStop.WindowClosed,
        WardAcknowledgement.LayoutMismatch => WardScanStop.LayoutMismatch,
        WardAcknowledgement.ScopeChanged => WardScanStop.ScopeChanged,
        WardAcknowledgement.NoPacketObserver => WardScanStop.NoPacketObserver,
        _ => WardScanStop.TimedOut,
    };
}
