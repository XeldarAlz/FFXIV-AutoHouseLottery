using clib.TaskSystem;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace AutoHouseLottery.Core.Tasks;

internal abstract partial class AutoCommon : TaskBase
{
    private const int DelayPollFrames = 2;
    private const int WaitPollFrames = 30;

    protected void Diag(string message, [CallerFilePath] string callerFile = "") => RunLog.Info(message, callerFile);

    protected void Warn(string message, [CallerFilePath] string callerFile = "") => RunLog.Warning(message, callerFile);

    protected new async Task DelayMs(int milliseconds)
    {
        var deadline = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return;
            }

            await NextFrame(DelayPollFrames);
        }
    }

    protected async Task<bool> WaitUntilTimed(Func<bool> condition, int timeoutMs, string scope, int checkFrames = WaitPollFrames)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        var threw = false;
        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }

            if (ConditionHolds(condition, scope, ref threw))
            {
                return true;
            }

            await NextFrame(checkFrames);
        }

        Diag($"WAIT TIMEOUT: '{scope}' not satisfied within {timeoutMs / TimeUnits.MillisecondsPerSecond}s");
        return false;
    }

    private bool ConditionHolds(Func<bool> condition, string scope, ref bool threw)
    {
        try
        {
            return condition();
        }
        catch (Exception exception)
        {
            if (!threw)
            {
                Warn($"WaitUntilTimed '{scope}' condition threw (treating as unsatisfied; will retry until timeout): {exception.Message}");
                threw = true;
            }

            return false;
        }
    }
}
