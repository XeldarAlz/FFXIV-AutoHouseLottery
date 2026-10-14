using System.Numerics;
using System.Threading.Tasks;
using AutoHouseLottery.Core.Game;
using AutoHouseLottery.Core.Game.Dialogs;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Game.Placard;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Integration;
using AutoHouseLottery.Core.Ipc;
using AutoHouseLottery.Core.Travel;
using AutoHouseLottery.Core.Travel.Model;
using ECommons;
using ECommons.DalamudServices;

namespace AutoHouseLottery.Core.Tasks;

internal enum VisitFailure : byte
{
    None,
    CharacterChanged,
    TravelFailed,
    TravelUnavailable,
    DependencyBusy,
    WrongDestination,
    PlacardNotFound,
    PlacardNotOpened,
}

// The one visit to a plot's placard that the entry run and the settlement driver share: travel with one retry, the
// game's own arrival check, the walk to the placard, opening it, and reading it once its sources agree on the address
// and its sale data has arrived. The placard's identity comes from that reading, never from distance. Travel the task
// started and still owns is cancelled by ReleaseVisit.
internal abstract partial class AutoCommon
{
    // One retry of a conclusively failed travel leg before the player is asked (TECHNICAL_DESIGN section 12).
    private const int VisitTravelAttempts = 2;
    private const int VisitObservePollFrames = 15;
    private const int VisitDependencyBusyWaitMs = 30_000;
    private const int VisitManualArrivalWaitMs = 300_000;
    private const int VisitApproachWatchdogMs = 45_000;
    private const int VisitPlacardOpenWaitMs = 30_000;
    private const int VisitPlacardInteractAttempts = 3;
    private const int VisitPlacardInteractGapMs = 3_000;
    private const int VisitPlacardSettleWaitMs = 5_000;

    private long visitTravelToken;

    // Travel until the game shows the character in the plot's ward and division, or the player walks there. beforeRetry
    // runs before the one retry of a failed leg. The arrival itself is judged by VerifyArrival.
    protected async Task<VisitFailure> TravelToPlot(HousingDestination destination, ulong contentId, bool manualArrival, string label, Action? beforeRetry = null)
    {
        if (Svc.PlayerState.ContentId != contentId)
        {
            return VisitFailure.CharacterChanged;
        }

        if (ArrivalCheck.Evaluate(destination, HousingTravel.ObserveArrival()) == ArrivalVerdict.Arrived)
        {
            return VisitFailure.None;
        }

        return manualArrival ? await AwaitManualArrival(destination, label) : await TravelWithRetry(destination, label, beforeRetry);
    }

    protected VisitFailure VerifyArrival(HousingDestination destination, string label)
    {
        var verdict = ArrivalCheck.Evaluate(destination, HousingTravel.ObserveArrival());
        Diag($"{label}: arrival at {destination} is {verdict}.");
        return verdict == ArrivalVerdict.Arrived ? VisitFailure.None : VisitFailure.WrongDestination;
    }

    protected async Task<VisitFailure> OpenPlacardAt(HousingDestination destination, HousingObservers observers, bool manualArrival, string label)
    {
        if (AddonReads.IsReady(PlacardReader.AddonName))
        {
            var open = PlacardReader.Read(observers.PlacardWindow, observers.PlacardSales, observers.SignboardLayout);
            if (PlacardIdentity.Verify(destination, open?.Address) == PlacardIdentityVerdict.Confirmed)
            {
                return VisitFailure.None;
            }

            Diag($"{label}: an open placard does not show {destination}; closing it first.");
            HousingDialogs.ClosePlacard();
            await WaitUntilTimed(() => !AddonReads.IsReady(PlacardReader.AddonName), VisitPlacardSettleWaitMs, "visit-close-other-placard");
        }

        var approach = new PlacardApproach(destination);
        await RunCancellable(approach, VisitApproachWatchdogMs, "visit-placard-approach");
        CancelToken.ThrowIfCancellationRequested();
        if (approach.Outcome is ApproachOutcome.InRange or ApproachOutcome.Walked && approach.PlacardPosition is { } seenAt && await Interact(seenAt, label))
        {
            return VisitFailure.None;
        }

        if (manualArrival)
        {
            Status = "Open the plot's placard";
            Diag($"{label}: waiting for the placard to be opened by hand.");
            return await WaitUntilTimed(() => AddonReads.IsReady(PlacardReader.AddonName), VisitManualArrivalWaitMs, "visit-manual-placard")
                ? VisitFailure.None
                : VisitFailure.PlacardNotOpened;
        }

        return approach.Outcome == ApproachOutcome.PlacardNotFound ? VisitFailure.PlacardNotFound : VisitFailure.PlacardNotOpened;
    }

    // The open placard once its sources agree on its address and its sale data has arrived, merged into the plot store and
    // the calendar. Proved is the cycle position its phase end proves, null when it proves none.
    protected async Task<(PlacardReading? Reading, CyclePosition? Proved)> ReadPlacardSettled(HousingObservers observers, HousingContext housing)
    {
        await WaitUntilTimed(
            () => PlacardReader.Read(observers.PlacardWindow, observers.PlacardSales, observers.SignboardLayout) is { Agreement: AddressAgreement.Agree } settled
                && (settled.Sale is not null || settled.HousingType is not null),
            VisitPlacardSettleWaitMs,
            "visit-placard-reading");
        if (PlacardReader.Read(observers.PlacardWindow, observers.PlacardSales, observers.SignboardLayout) is not { } placard)
        {
            return (null, null);
        }

        return (placard, housing.ObservePlacard(placard, Svc.PlayerState.CurrentWorld.RowId));
    }

    protected void ReleaseVisit()
    {
        if (visitTravelToken == 0)
        {
            return;
        }

        HousingTravel.CancelOwned(visitTravelToken);
        visitTravelToken = 0;
    }

    private async Task<VisitFailure> TravelWithRetry(HousingDestination destination, string label, Action? beforeRetry)
    {
        for (var attempt = 1; ; attempt++)
        {
            var failure = await TravelOnce(destination, label);
            if (failure != VisitFailure.TravelFailed || attempt >= VisitTravelAttempts)
            {
                return failure;
            }

            Diag($"{label}: travel to {destination} failed before anything was committed; trying once more.");
            beforeRetry?.Invoke();
        }
    }

    private async Task<VisitFailure> TravelOnce(HousingDestination destination, string label)
    {
        var start = HousingTravel.Begin(destination);
        if (start.Result == TravelStartResult.DependencyBusy)
        {
            Diag($"{label}: Lifestream is busy with other work; waiting up to {VisitDependencyBusyWaitMs / TimeUnits.MillisecondsPerSecond}s for it.");
            await WaitUntilTimed(() => LifestreamIpc.Instance.IsBusy() == false, VisitDependencyBusyWaitMs, "visit-lifestream-idle");
            start = HousingTravel.Begin(destination);
        }

        switch (start.Result)
        {
            case TravelStartResult.Started:
                break;
            case TravelStartResult.DependencyBusy or TravelStartResult.AlreadyTravelling:
                return VisitFailure.DependencyBusy;
            case TravelStartResult.NotInstalled:
                return VisitFailure.TravelUnavailable;
            default:
                return VisitFailure.TravelFailed;
        }

        visitTravelToken = start.Token;
        var state = TravelState.Travelling;
        while (state == TravelState.Travelling)
        {
            await NextFrame(VisitObservePollFrames);
            state = HousingTravel.Observe(start.Token);
        }

        visitTravelToken = 0;
        Diag($"{label}: travel {start.Token} ended {state}.");
        return state switch
        {
            TravelState.Arrived => VisitFailure.None,
            TravelState.TakenOver => VisitFailure.DependencyBusy,
            _ => VisitFailure.TravelFailed,
        };
    }

    // Without travel, or after the player chose to walk, the visit waits for the game to show the character at the plot.
    private async Task<VisitFailure> AwaitManualArrival(HousingDestination destination, string label)
    {
        Status = "Waiting for you at the plot";
        Diag($"{label}: waiting up to {VisitManualArrivalWaitMs / TimeUnits.MillisecondsPerSecond}s for the character to reach {destination}.");
        var arrived = await WaitUntilTimed(() => ArrivalCheck.Evaluate(destination, HousingTravel.ObserveArrival()) == ArrivalVerdict.Arrived, VisitManualArrivalWaitMs, "visit-manual-arrival");
        return arrived ? VisitFailure.None : VisitFailure.TravelFailed;
    }

    private async Task<bool> Interact(Vector3 seenAt, string label)
    {
        for (var attempt = 1; attempt <= VisitPlacardInteractAttempts; attempt++)
        {
            if (PlacardInteraction.Find(seenAt) is not { } placard || !PlacardInteraction.InRange(placard))
            {
                Diag($"{label}: the placard seen at {seenAt} is gone or out of reach.");
                return false;
            }

            if (!GenericHelpers.IsOccupied())
            {
                Diag($"{label}: opening the placard (attempt {attempt}/{VisitPlacardInteractAttempts}).");
                PlacardInteraction.Open(placard);
            }

            if (await WaitUntilTimed(() => AddonReads.IsReady(PlacardReader.AddonName), attempt == VisitPlacardInteractAttempts ? VisitPlacardOpenWaitMs : VisitPlacardInteractGapMs, "visit-placard-open"))
            {
                return true;
            }
        }

        return false;
    }
}
