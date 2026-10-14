using AutoHouseLottery.Core.Ipc;
using AutoHouseLottery.Core.Tasks;
using AutoHouseLottery.Core.Travel.Model;
using clib.TaskSystem;
using Dalamud.Game.ClientState.Objects.Enums;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoHouseLottery.Core.Travel;

// The last metres to the target plot's placard after travel. It walks with vnavmesh when that is installed, under a
// deadline and a stall check; without it, or when the walk fails, the player is asked to walk up to the placard.
internal sealed class PlacardApproach(HousingDestination destination) : AutoCommon
{
    // EObjName row 2002736 is "placard" (ffxiv-datamining); the plot placards are event objects of that base ID.
    public const uint PlacardBaseId = 2002736;

    // Estimates to confirm in game: how far a plot's placard stands from Lifestream's entrance point, and how close the
    // character must be to interact with a placard.
    public const float InteractionRangeMeters = 3.5f;
    private const float PlacardSearchMeters = 25f;
    private const float WalkToleranceMeters = 2.5f;
    private const int ApproachDeadlineMs = 30_000;

    public ApproachOutcome Outcome { get; private set; } = ApproachOutcome.Cancelled;

    public PlacardAnchor Anchor { get; private set; }

    public Vector3? PlacardPosition { get; private set; }

    protected override async Task Execute()
    {
        Status = "Approaching the placard";
        if (ArrivalCheck.Evaluate(destination, HousingTravel.ObserveArrival()) != ArrivalVerdict.Arrived)
        {
            Finish(ApproachOutcome.NotAtDestination);
            return;
        }

        if (FindPlacard() is not { } placard)
        {
            Finish(ApproachOutcome.PlacardNotFound);
            return;
        }

        PlacardPosition = placard;
        if (WithinInteractionRange(placard))
        {
            Finish(ApproachOutcome.InRange);
            return;
        }

        if (!NavmeshIpc.Instance.Available)
        {
            Finish(ApproachOutcome.ManualArrivalNeeded);
            return;
        }

        const string label = "placard-approach";
        var stalled = false;
        var walk = new MoveOp(move => move.MoveInZone(placard, MovementConfig.Default.WithTolerance(WalkToleranceMeters), () => WithinInteractionRange(placard)));
        var completed = await RunCancellable(walk, ApproachDeadlineMs, label, StuckDetector.MoveStallAbort(label, () => stalled = true));
        if (CancelToken.IsCancellationRequested)
        {
            Finish(ApproachOutcome.Cancelled);
            return;
        }

        if (WithinInteractionRange(placard))
        {
            Finish(ApproachOutcome.Walked);
            return;
        }

        if (walk.Fault is { } fault)
        {
            Warn($"Placard approach: the walk failed ({fault.Message}).");
        }

        Finish(stalled || completed ? ApproachOutcome.Stuck : ApproachOutcome.TimedOut);
    }

    private Vector3? FindPlacard()
    {
        if (Svc.Objects.LocalPlayer is not { } player)
        {
            return null;
        }

        var entrance = LifestreamIpc.Instance.PlotEntrance(destination.TerritoryTypeId, destination.PlotIndex);
        Anchor = entrance is null ? PlacardAnchor.Player : PlacardAnchor.PlotEntrance;
        var anchor = entrance ?? player.Position;
        var candidates = new List<Vector3>();
        var objects = Svc.Objects;
        for (var objectIndex = 0; objectIndex < objects.Length; objectIndex++)
        {
            var candidate = objects[objectIndex];
            if (candidate is { ObjectKind: ObjectKind.EventObj, BaseId: PlacardBaseId, IsTargetable: true })
            {
                candidates.Add(candidate.Position);
            }
        }

        var nearest = PlacardSelection.Nearest(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(candidates), anchor, PlacardSearchMeters);
        Diag($"Placard approach: {candidates.Count} placard(s) nearby, anchored on the {Anchor} at {anchor}; {(nearest == PlacardSelection.NoCandidate ? "none" : $"chose {candidates[nearest]}")}.");
        return nearest == PlacardSelection.NoCandidate ? null : candidates[nearest];
    }

    private static bool WithinInteractionRange(Vector3 placard)
        => Svc.Objects.LocalPlayer is { } player && Vector3.Distance(player.Position, placard) <= InteractionRangeMeters;

    private void Finish(ApproachOutcome outcome)
    {
        Outcome = outcome;
        Diag($"Placard approach to {destination}: {outcome}.");
    }
}
