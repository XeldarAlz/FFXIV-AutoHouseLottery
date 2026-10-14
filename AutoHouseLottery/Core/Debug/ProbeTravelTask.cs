using AutoHouseLottery.Core.Game;
using AutoHouseLottery.Core.Game.Placard;
using AutoHouseLottery.Core.Tasks;
using AutoHouseLottery.Core.Travel;
using AutoHouseLottery.Core.Travel.Model;
using System.Threading.Tasks;

namespace AutoHouseLottery.Core.Debug;

// The probe's travel check: Lifestream travel to a plot under a token, the arrival verdict from the game's housing
// state, the walk to the nearest placard, then, once the player opens it, the plot's identity from the placard reading.
// It never opens the placard or presses anything.
internal sealed class ProbeTravelTask(HousingDestination destination, bool travel, HousingObservers observers) : AutoCommon
{
    private const int ObservePollFrames = 15;
    private const int ApproachWatchdogMs = 45_000;
    private const int PlacardOpenWaitMs = 60_000;

    protected override async Task Execute()
    {
        if (travel && !await Travel())
        {
            return;
        }

        Status = "Approaching the placard";
        var approach = new PlacardApproach(destination);
        await RunCancellable(approach, ApproachWatchdogMs, "probe-approach");
        Diag($"Probe: approach ended {approach.Outcome}, anchored on the {approach.Anchor}, placard at {approach.PlacardPosition?.ToString() ?? "none"}.");
        if (approach.Outcome is not (ApproachOutcome.InRange or ApproachOutcome.Walked))
        {
            Diag("Probe: walk up to the placard yourself to continue.");
            return;
        }

        Status = "Open the placard to confirm the plot";
        Diag("Probe: open the placard; its address confirms whether this is the plot.");
        if (!await WaitUntilTimed(() => AddonReads.IsReady(PlacardReader.AddonName), PlacardOpenWaitMs, "probe-placard-open"))
        {
            Diag("Probe: the placard was not opened; the plot's identity stays unverified.");
            return;
        }

        var reading = PlacardReader.Read(observers.PlacardWindow, observers.PlacardSales, observers.SignboardLayout);
        var verdict = PlacardIdentity.Verify(destination, reading?.Address);
        Diag($"Probe: placard identity {verdict} for {destination} (sources {reading?.Agreement.ToString() ?? "unread"}).");
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
        try
        {
            while (state == TravelState.Travelling)
            {
                await NextFrame(ObservePollFrames);
                state = HousingTravel.Observe(start.Token);
            }
        }
        finally
        {
            // NextFrame throws once the task is stopped; the travel slot is released here either way.
            if (state == TravelState.Travelling)
            {
                HousingTravel.CancelOwned(start.Token);
            }
        }

        Diag($"Probe: travel {start.Token} ended {state}.");
        return state == TravelState.Arrived;
    }
}
