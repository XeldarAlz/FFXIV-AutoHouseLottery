using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;
using System.Numerics;

namespace AutoHouseLottery.Core.Ipc;

internal sealed class NavmeshIpc
{
    public const int WaypointsUnavailable = -1;

    private const string IsRunningFailed = "Navmesh IsRunning failed";
    private const string SimpleMovePathfindFailed = "Navmesh SimpleMove.PathfindInProgress failed";
    private const string NavPathfindFailed = "Navmesh Nav.PathfindInProgress failed";
    private const string NumWaypointsFailed = "Navmesh NumWaypoints failed";
    private const string ListWaypointsFailed = "Navmesh ListWaypoints failed";

    private static NavmeshIpc? instance;

    private readonly ICallGateSubscriber<bool> navIsReady;
    private readonly ICallGateSubscriber<bool> pathIsRunning;
    private readonly ICallGateSubscriber<bool> simpleMovePathfindInProgress;
    private readonly ICallGateSubscriber<bool> navPathfindInProgress;
    private readonly ICallGateSubscriber<int> pathNumWaypoints;
    private readonly ICallGateSubscriber<List<Vector3>> pathListWaypoints;

    // Cached once so the per-frame stall checks do not allocate a delegate on every call.
    private readonly Func<bool> isRunningCall;
    private readonly Func<bool> simpleMovePathfindInProgressCall;
    private readonly Func<bool> navPathfindInProgressCall;
    private readonly Func<int> numWaypointsCall;
    private readonly Func<List<Vector3>?> listWaypointsCall;

    private NavmeshIpc()
    {
        var pluginInterface = Svc.PluginInterface;
        navIsReady = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        pathIsRunning = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        simpleMovePathfindInProgress = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
        navPathfindInProgress = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.PathfindInProgress");
        pathNumWaypoints = pluginInterface.GetIpcSubscriber<int>("vnavmesh.Path.NumWaypoints");
        pathListWaypoints = pluginInterface.GetIpcSubscriber<List<Vector3>>("vnavmesh.Path.ListWaypoints");

        isRunningCall = pathIsRunning.InvokeFunc;
        simpleMovePathfindInProgressCall = simpleMovePathfindInProgress.InvokeFunc;
        navPathfindInProgressCall = navPathfindInProgress.InvokeFunc;
        numWaypointsCall = pathNumWaypoints.InvokeFunc;
        listWaypointsCall = pathListWaypoints.InvokeFunc;
    }

    public static NavmeshIpc Instance => instance ??= new NavmeshIpc();

    // vnavmesh registers its gates when it loads, so a missing IsReady gate means it is not installed or not loaded.
    public bool Available => navIsReady.HasFunction;

    public bool IsRunning()
        => IpcGate.Invoke(pathIsRunning.HasFunction, isRunningCall, false, IsRunningFailed);

    public bool IsPathfinding()
        => IpcGate.Invoke(simpleMovePathfindInProgress.HasFunction, simpleMovePathfindInProgressCall, false, SimpleMovePathfindFailed)
        || IpcGate.Invoke(navPathfindInProgress.HasFunction, navPathfindInProgressCall, false, NavPathfindFailed);

    public int NumWaypoints()
        => IpcGate.Invoke(pathNumWaypoints.HasFunction, numWaypointsCall, WaypointsUnavailable, NumWaypointsFailed);

    // The call copies the whole waypoint list, so callers cache the answer.
    public Vector3? CurrentWaypoint()
    {
        var waypoints = IpcGate.Invoke(pathListWaypoints.HasFunction, listWaypointsCall, null, ListWaypointsFailed);
        return waypoints is { Count: > 0 } ? waypoints[0] : null;
    }
}
