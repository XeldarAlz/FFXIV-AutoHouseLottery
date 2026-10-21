using AutoHouseLottery.Core.Ipc;
using clib.Extensions;
using clib.TaskSystem;
using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading.Tasks;
using OnDispose = clib.Services.OnDispose;

namespace AutoHouseLottery.Core.Tasks;

// The library's own movement methods live on TaskBase, which installs two native movement hooks per instance for its
// straight-line walk. This is the same in-zone walk on top of vnavmesh alone, so runs and placard walks can be created
// for a whole session without exhausting the hook allocator. The only walk is the last metres to a placard inside a
// ward, so the library's mount, dismount, teleport and aethernet steps are not carried over.
internal sealed partial class MoveOp
{
    private const float DefaultArriveMeters = 3f;

    private static NavmeshIpc Navmesh => NavmeshIpc.Instance;

    private static IPlayerCharacter? Player => Svc.Objects.LocalPlayer;

    private async Task Navigate(Vector3 destination, MovementConfig config, Func<bool>? stopCondition)
    {
        await WaitUntil(() => Player.Available, "WaitingForPlayer");
        var tolerance = Math.Max(config.Tolerance ?? 0f, Navmesh.Tolerance());
        if (Player.WithinRange(destination, tolerance) || stopCondition?.Invoke() == true)
        {
            return;
        }

        Status = "Waiting for the navmesh";
        await WaitUntil(() => Navmesh.IsReady() || Navmesh.BuildProgress() >= 0f, "WaitingForNavmesh");
        await WaitWhile(() => Navmesh.BuildProgress() >= 0f, "BuildingNavmesh");
        ErrorIf(!Navmesh.IsReady(), "Could not load the navmesh for this zone");
        await WaitWhile(Navmesh.IsPathfinding, "WaitingForPathfinding");
        CancelToken.ThrowIfCancellationRequested();

        // Registered with this operation, so Cancel stops the path at once instead of leaving it running.
        using (new OnDispose(StopOwnedPath))
        {
            ErrorIf(!Navmesh.PathfindAndMoveCloseTo(destination, Player.InFlight, config.Tolerance ?? DefaultArriveMeters), $"Could not start pathfinding to {destination}");
            Status = $"Moving to {destination}";
            await NextFrame();
            await WaitWhile(
                () => !Player.WithinRange(destination, tolerance) && stopCondition?.Invoke() != true && Navmesh.IsBusy(),
                "Navigate");
            if (stopCondition?.Invoke() == true)
            {
                await StopPathfinding();
            }
        }
    }

    private async Task StopPathfinding()
    {
        Navmesh.Stop();
        if (!Navmesh.IsPathfinding())
        {
            return;
        }

        await WaitWhile(Navmesh.IsPathfinding, "StopPathfinding");
        Navmesh.Stop();
    }

    private static void StopOwnedPath() => NavmeshIpc.Instance.Stop();
}
