namespace AutoHouseLottery.Core.Tasks;

// Starts a task of its own, outside a run. Building a task can hook game functions and throw when one is missing; that
// must not reach a command handler, so it ends in the log instead.
internal static class TaskStarter
{
    // Completed runs on the framework thread once the task has ended, however it ended.
    public static bool TryStart(Func<AutoCommon> build, Action<AutoCommon>? completed = null)
    {
        if (Busy)
        {
            RunLog.Warning($"A task is already running ({clib.Services.Svc.Automation.Name}); not starting another.");
            return false;
        }

        try
        {
            var task = build();
            clib.Services.Svc.Automation.Start(task, OnCompleted: completed is null ? null : () => _ = ECommons.DalamudServices.Svc.Framework.RunOnFrameworkThread(() => completed(task)));
            return true;
        }
        catch (Exception exception)
        {
            RunLog.Error(exception, "A task could not be started");
            return false;
        }
    }

    public static bool Busy => clib.Services.Svc.Automation.Running;

    public static void StopCurrent() => clib.Services.Svc.Automation.Stop();
}
