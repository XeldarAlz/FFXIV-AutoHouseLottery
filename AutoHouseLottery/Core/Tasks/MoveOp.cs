using clib.TaskSystem;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoHouseLottery.Core.Tasks;

// One library movement run as its own task, so it owns its cancellation source. The parent can cancel exactly this
// operation, and cancelling fires its registered cleanups instead of leaving it running.
internal sealed class MoveOp(Func<MoveOp, Task> body) : TaskBase
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
        => MoveTo(destination, config, allowTeleportIfFaster: false, stopCondition, null, allowAethernet: false);
}
