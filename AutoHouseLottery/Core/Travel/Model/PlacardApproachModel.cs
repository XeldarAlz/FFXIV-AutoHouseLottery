using System.Numerics;

namespace AutoHouseLottery.Core.Travel.Model;

internal enum ApproachOutcome : byte
{
    InRange,
    Walked,
    ManualArrivalNeeded,
    NotAtDestination,
    PlacardNotFound,
    Stuck,
    TimedOut,
    Cancelled,
}

// PlotEntrance: Lifestream's entrance point for the plot, so the nearest placard to it is the plot's own. Player: no
// entrance is known, so the nearest placard to the character is only a candidate until the placard reader confirms it.
internal enum PlacardAnchor : byte
{
    PlotEntrance,
    Player,
}

internal static class PlacardSelection
{
    public const int NoCandidate = -1;

    public static int Nearest(ReadOnlySpan<Vector3> candidates, Vector3 anchor, float maxDistanceMeters)
    {
        var best = NoCandidate;
        var bestDistance = maxDistanceMeters * maxDistanceMeters;
        for (var candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
        {
            var distance = Vector3.DistanceSquared(candidates[candidateIndex], anchor);
            if (distance > bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            best = candidateIndex;
        }

        return best;
    }
}
