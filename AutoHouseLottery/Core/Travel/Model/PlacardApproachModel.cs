using AutoHouseLottery.Core.Game.Model;
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

// Where the search for the plot's placard is centred: Lifestream's entrance point for the plot, or the character when
// Lifestream has none. Either way the nearest placard is only a candidate; distance is not identity, and the plot is
// confirmed only by the placard reading's agreed address once the placard is open (PlacardIdentity).
internal enum PlacardAnchor : byte
{
    PlotEntrance,
    Player,
}

internal enum PlacardIdentityVerdict : byte
{
    Confirmed,
    WrongPlot,
    Unverified,
}

internal static class PlacardIdentity
{
    // Unverified when the placard's sources do not agree on an address, so a single unconfirmed source never passes.
    public static PlacardIdentityVerdict Verify(in HousingDestination destination, PlotAddress? agreedAddress)
    {
        if (agreedAddress is not { } address)
        {
            return PlacardIdentityVerdict.Unverified;
        }

        return address.TerritoryTypeId == destination.TerritoryTypeId && address.WardIndex == destination.WardIndex && address.PlotIndex == destination.PlotIndex
            ? PlacardIdentityVerdict.Confirmed
            : PlacardIdentityVerdict.WrongPlot;
    }
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
