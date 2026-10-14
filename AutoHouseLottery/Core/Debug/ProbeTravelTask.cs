using AutoHouseLottery.Core.Tasks;
using AutoHouseLottery.Core.Travel;
using AutoHouseLottery.Core.Travel.Model;
using System.Threading.Tasks;

namespace AutoHouseLottery.Core.Debug;

// The probe's travel check: Lifestream travel to a plot under a token, the arrival verdict from the game's housing
// state, then the walk to the plot's placard. It never opens the placard or presses anything.
internal sealed class ProbeTravelTask(HousingDestination destination, bool travel) : AutoCommon
{
    private const int ObservePollFrames = 15;
    private const int ApproachWatchdogMs = 45_000;

    protected override async Task Execute()
    {
        if (travel && !await Travel())
        {
            return;
        }

        Status = "Approaching the placard";
        var approach = new PlacardApproach(destination);
        await RunCancellable(approach, ApproachWatchdogMs, "probe-approach");
        Diag($"Probe: approach ended {approach.Outcome}, anchored on the {approach.Anchor}, placard at {approach.PlacardPosition?.ToString() ?? "none"}. "
           + (approach.Outcome is ApproachOutcome.InRange or ApproachOutcome.Walked
               ? "Open the placard yourself, then run /ahl probe to read it."
               : "Walk up to the placard yourself to continue."));
    }

    private async Task<bool> Travel()
    {
        Status = "Travelling with Lifestream";
        var start = HousingTravel.Begin(destination);
        if (start.Result != TravelStartResult.Started)
        {
            Diag($"Probe: travel to {destination} not started ({start.Result}).");
            return false;
        }

        var state = TravelState.Travelling;
        while (state == TravelState.Travelling)
        {
            if (CancelToken.IsCancellationRequested)
            {
                HousingTravel.CancelOwned(start.Token);
                return false;
            }

            await NextFrame(ObservePollFrames);
            state = HousingTravel.Observe(start.Token);
        }

        Diag($"Probe: travel {start.Token} ended {state}.");
        return state == TravelState.Arrived;
    }
}
