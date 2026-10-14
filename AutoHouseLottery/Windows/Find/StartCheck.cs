using AutoHouseLottery.Core;
using AutoHouseLottery.Core.Selection.Model;
using AutoHouseLottery.Core.Tasks;
using AutoHouseLottery.Core.Tasks.Model;

namespace AutoHouseLottery.Windows.Find;

// The run controller's answer to "could this start now", shown under an Enter button. Asking plans the run, so it is
// asked again only when the policy changes or a second has passed; the answer is empty when nothing stands in the way.
internal sealed class StartCheck
{
    private const long RecheckMs = TimeUnits.MillisecondsPerSecond;

    private RunPolicy? asked;
    private long askedAtMs;
    private string reason = string.Empty;

    public string Reason(RunController controller, RunMode mode, RunPolicy policy)
    {
        var now = Environment.TickCount64;
        if (ReferenceEquals(policy, asked) && now - askedAtMs < RecheckMs)
        {
            return reason;
        }

        asked = policy;
        askedAtMs = now;
        var refusal = controller.Check(mode, policy);
        reason = refusal == StartRefusal.None ? string.Empty : RunText.Refusal(refusal);
        return reason;
    }

    public void Invalidate() => asked = null;
}
