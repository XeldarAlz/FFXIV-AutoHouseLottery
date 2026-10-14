namespace AutoHouseLottery.Core.Travel.Model;

// Requested: the travel request was sent and Lifestream has not been seen busy yet. Running: busy since the request,
// so the work is ours. Finished: idle again after running. NeverStarted: never busy within the start grace.
// TakenOver: busy again after it was ours and stopped, which is somebody else's work.
internal enum TravelPhase : byte
{
    Requested,
    Running,
    Finished,
    NeverStarted,
    TakenOver,
}

// Local bookkeeping only: Lifestream offers no lease, so ownership is inferred from its busy flag around our request.
internal sealed class TravelOwnership(long requestedAtMs)
{
    public const int StartGraceMs = 3_000;

    public TravelPhase Phase { get; private set; } = TravelPhase.Requested;

    // A busy flag that could not be read (null) moves nothing: it is neither proof of idle nor of somebody else's work.
    public void Observe(bool? dependencyBusy, long nowMs)
    {
        switch (Phase)
        {
            case TravelPhase.Requested when dependencyBusy == true:
                Phase = TravelPhase.Running;
                break;
            case TravelPhase.Requested when dependencyBusy == false && nowMs - requestedAtMs >= StartGraceMs:
                Phase = TravelPhase.NeverStarted;
                break;
            case TravelPhase.Running when dependencyBusy == false:
                Phase = TravelPhase.Finished;
                break;
            case TravelPhase.Finished or TravelPhase.NeverStarted when dependencyBusy == true:
                Phase = TravelPhase.TakenOver;
                break;
        }
    }

    // Abort is Lifestream's global cancel, so it is used only while the busy work is provably the one we started.
    public bool MayAbort(bool? dependencyBusy) => Phase == TravelPhase.Running && dependencyBusy == true;
}

internal enum TravelState : byte
{
    Travelling,
    Arrived,
    WrongDestination,
    NotStarted,
    TakenOver,
    TimedOut,
    Cancelled,
}

internal static class TravelProgressRule
{
    // Lifestream going idle is not arrival: the game's housing state has to agree, and a zone still loading is waited out.
    public static TravelState Evaluate(TravelPhase phase, ArrivalVerdict verdict, bool cancelled, bool pastDeadline)
    {
        if (cancelled)
        {
            return TravelState.Cancelled;
        }

        if (phase == TravelPhase.TakenOver)
        {
            return TravelState.TakenOver;
        }

        if (phase is TravelPhase.Finished or TravelPhase.NeverStarted)
        {
            if (verdict == ArrivalVerdict.Arrived)
            {
                return TravelState.Arrived;
            }

            if (verdict != ArrivalVerdict.Unknown)
            {
                return phase == TravelPhase.NeverStarted ? TravelState.NotStarted : TravelState.WrongDestination;
            }
        }

        return pastDeadline ? TravelState.TimedOut : TravelState.Travelling;
    }
}
