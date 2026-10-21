using clib.TaskSystem;
using System.Numerics;
using System.Threading.Tasks;
using AutoTask = clib.Services.AutoTask;

namespace AutoHouseLottery.Core.Tasks;

// One movement run as its own task, so it owns its cancellation source. The parent can cancel exactly this operation,
// and cancelling fires its registered cleanups instead of leaving it running. It builds on the plain task base rather
// than the library's TaskBase, whose every instance installs two native movement hooks.
internal sealed partial class MoveOp(Func<MoveOp, Task> body) : AutoTask
{
    // The task runner swallows exceptions, so a failed move would otherwise look like a clean arrival.
    public Exception? Fault { get; private set; }

    protected override async Task Execute()
    {
        try
        {
            await body(this);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Fault = exception;
        }
    }

    public Task MoveInZone(Vector3 destination, MovementConfig config, Func<bool>? stopCondition)
        => Navigate(destination, config, stopCondition);
}
