using System.Threading.Tasks;
using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Game;
using AutoHouseLottery.Core.Game.Character;
using AutoHouseLottery.Core.Game.Dialogs;
using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Game.Placard;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Integration.Model;
using AutoHouseLottery.Core.Ipc;
using AutoHouseLottery.Core.Tasks.Model;
using AutoHouseLottery.Core.Travel;
using AutoHouseLottery.Core.Travel.Model;
using ECommons;
using ECommons.DalamudServices;

namespace AutoHouseLottery.Core.Tasks;

internal sealed partial class EntryRunTask
{
    // One retry of a conclusively failed travel leg before the player is asked (TECHNICAL_DESIGN section 12).
    private const int MaxTravelAttempts = 2;
    private const int DependencyBusyWaitMs = 30_000;
    private const int ManualArrivalWaitMs = 300_000;
    private const int ApproachWatchdogMs = 45_000;
    private const int PlacardOpenWaitMs = 30_000;
    private const int PlacardInteractAttempts = 3;
    private const int PlacardInteractGapMs = 3_000;
    private const int PlacardSettleWaitMs = 5_000;

    private enum TravelLeg : byte { Arrived, Failed, Busy, Unavailable }

    private async Task<EntryFailure> TravelToTarget()
    {
        Move(EntryStep.Travel);
        var destination = DestinationOf(session.Target);
        if (Svc.PlayerState.ContentId != session.Settings.ContentId)
        {
            return EntryFailure.CharacterChanged;
        }

        if (ArrivalCheck.Evaluate(destination, HousingTravel.ObserveArrival()) != ArrivalVerdict.Arrived)
        {
            var failure = session.ManualArrival ? await AwaitManualArrival(destination) : await TravelWithRetry(destination);
            if (failure != EntryFailure.None)
            {
                return failure;
            }
        }

        Move(EntryStep.VerifyArrival);
        var verdict = ArrivalCheck.Evaluate(destination, HousingTravel.ObserveArrival());
        Diag($"Entry run {session.RunId}: arrival at {destination} is {verdict}.");
        return verdict == ArrivalVerdict.Arrived ? EntryFailure.None : EntryFailure.WrongDestination;
    }

    private async Task<EntryFailure> TravelWithRetry(HousingDestination destination)
    {
        while (true)
        {
            session.TravelAttempts++;
            var leg = await TravelOnce(destination);
            switch (leg)
            {
                case TravelLeg.Arrived:
                    return EntryFailure.None;
                case TravelLeg.Busy:
                    return EntryFailure.DependencyBusy;
                case TravelLeg.Unavailable:
                    return EntryFailure.TravelUnavailable;
            }

            if (session.TravelAttempts >= MaxTravelAttempts)
            {
                return EntryFailure.TravelFailed;
            }

            Diag($"Entry run {session.RunId}: travel to {destination} failed before anything was committed; trying once more.");
            Move(EntryStep.Travel);
        }
    }

    private async Task<TravelLeg> TravelOnce(HousingDestination destination)
    {
        var start = HousingTravel.Begin(destination);
        if (start.Result == TravelStartResult.DependencyBusy)
        {
            Diag($"Entry run {session.RunId}: Lifestream is busy with other work; waiting up to {DependencyBusyWaitMs / TimeUnits.MillisecondsPerSecond}s for it.");
            await WaitUntilTimed(() => LifestreamIpc.Instance.IsBusy() == false, DependencyBusyWaitMs, "run-lifestream-idle");
            start = HousingTravel.Begin(destination);
        }

        switch (start.Result)
        {
            case TravelStartResult.Started:
                break;
            case TravelStartResult.DependencyBusy or TravelStartResult.AlreadyTravelling:
                return TravelLeg.Busy;
            case TravelStartResult.NotInstalled:
                return TravelLeg.Unavailable;
            default:
                return TravelLeg.Failed;
        }

        travelToken = start.Token;
        var state = TravelState.Travelling;
        while (state == TravelState.Travelling)
        {
            await NextFrame(ObservePollFrames);
            state = HousingTravel.Observe(start.Token);
        }

        travelToken = 0;
        Diag($"Entry run {session.RunId}: travel {start.Token} ended {state}.");
        return state switch
        {
            TravelState.Arrived => TravelLeg.Arrived,
            TravelState.TakenOver => TravelLeg.Busy,
            _ => TravelLeg.Failed,
        };
    }

    // Without travel, or after the player chose to walk, the run waits for the game to show the character at the plot.
    private async Task<EntryFailure> AwaitManualArrival(HousingDestination destination)
    {
        Status = "Waiting for you at the plot";
        Diag($"Entry run {session.RunId}: waiting up to {ManualArrivalWaitMs / TimeUnits.MillisecondsPerSecond}s for the character to reach {destination}.");
        var arrived = await WaitUntilTimed(() => ArrivalCheck.Evaluate(destination, HousingTravel.ObserveArrival()) == ArrivalVerdict.Arrived, ManualArrivalWaitMs, "run-manual-arrival");
        return arrived ? EntryFailure.None : EntryFailure.TravelFailed;
    }

    private async Task<EntryFailure> OpenPlacard()
    {
        Move(EntryStep.OpenPlacard);
        var destination = DestinationOf(session.Target);
        if (AddonReads.IsReady(PlacardReader.AddonName))
        {
            var observers = services.Observers;
            var open = PlacardReader.Read(observers.PlacardWindow, observers.PlacardSales, observers.SignboardLayout);
            if (PlacardIdentity.Verify(destination, open?.Address) == PlacardIdentityVerdict.Confirmed)
            {
                return EntryFailure.None;
            }

            Diag($"Entry run {session.RunId}: an open placard does not show {destination}; closing it first.");
            HousingDialogs.ClosePlacard();
            await WaitUntilTimed(() => !AddonReads.IsReady(PlacardReader.AddonName), PlacardSettleWaitMs, "run-close-other-placard");
        }

        var approach = new PlacardApproach(destination);
        await RunCancellable(approach, ApproachWatchdogMs, "run-placard-approach");
        if (CancelToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(CancelToken);
        }

        if (approach.Outcome is ApproachOutcome.InRange or ApproachOutcome.Walked && approach.PlacardPosition is { } seenAt && await Interact(seenAt))
        {
            return EntryFailure.None;
        }

        if (session.ManualArrival)
        {
            Status = "Open the plot's placard";
            Diag($"Entry run {session.RunId}: waiting for the placard to be opened by hand.");
            return await WaitUntilTimed(() => AddonReads.IsReady(PlacardReader.AddonName), ManualArrivalWaitMs, "run-manual-placard")
                ? EntryFailure.None
                : EntryFailure.PlacardNotOpened;
        }

        return approach.Outcome == ApproachOutcome.PlacardNotFound ? EntryFailure.PlacardNotFound : EntryFailure.PlacardNotOpened;
    }

    private async Task<bool> Interact(System.Numerics.Vector3 seenAt)
    {
        for (var attempt = 1; attempt <= PlacardInteractAttempts; attempt++)
        {
            if (PlacardInteraction.Find(seenAt) is not { } placard || !PlacardInteraction.InRange(placard))
            {
                Diag($"Entry run {session.RunId}: the placard seen at {seenAt} is gone or out of reach.");
                return false;
            }

            if (!GenericHelpers.IsOccupied())
            {
                Diag($"Entry run {session.RunId}: opening the placard (attempt {attempt}/{PlacardInteractAttempts}).");
                PlacardInteraction.Open(placard);
            }

            if (await WaitUntilTimed(() => AddonReads.IsReady(PlacardReader.AddonName), attempt == PlacardInteractAttempts ? PlacardOpenWaitMs : PlacardInteractGapMs, "run-placard-open"))
            {
                return true;
            }
        }

        return false;
    }

    // The placard's sources must agree on its address before anything it shows is believed.
    private async Task<(EntryFailure Failure, PlacardReading? Reading)> ReadLivePlot()
    {
        Move(EntryStep.ReadLivePlot);
        var observers = services.Observers;
        await WaitUntilTimed(
            () => PlacardReader.Read(observers.PlacardWindow, observers.PlacardSales, observers.SignboardLayout) is { Agreement: AddressAgreement.Agree } settled
                && (settled.Sale is not null || settled.HousingType is not null),
            PlacardSettleWaitMs,
            "run-placard-reading");
        if (PlacardReader.Read(observers.PlacardWindow, observers.PlacardSales, observers.SignboardLayout) is not { } placard)
        {
            return (EntryFailure.PlacardNotOpened, null);
        }

        var proved = services.Housing.ObservePlacard(placard, Svc.PlayerState.CurrentWorld.RowId);
        session.LiveCycle = proved?.Cycle;
        var facts = new LivePlotFacts(
            PlacardIdentity.Verify(DestinationOf(session.Target), placard.Address),
            placard.HousingType,
            placard.Sale,
            session.Settings.Buyer,
            EnterButtonUsable(placard),
            session.Policy.Cycle.Cycle,
            proved?.Cycle);
        var failure = LivePlotRules.Check(facts);
        Diag($"Entry run {session.RunId}: placard for {session.Target}: identity {facts.Identity} ({placard.Agreement}), housing {placard.HousingType?.ToString() ?? "unknown"}, "
           + $"sale {placard.Sale?.ToString() ?? "none"} from {placard.SaleSource}, Enter Lottery {(facts.EnterButtonUsable ? "usable" : "not usable")}, live cycle {proved?.Cycle.ToString() ?? "unproved"}; {failure}.");
        return (failure, placard);
    }

    private static bool EnterButtonUsable(PlacardReading placard)
    {
        var buttons = placard.Buttons;
        for (var buttonIndex = 0; buttonIndex < buttons.Length; buttonIndex++)
        {
            var button = buttons[buttonIndex];
            if (button.MatchedText == HousingText.EnterLotteryButton && button.Enabled && button.Visible)
            {
                return true;
            }
        }

        return false;
    }

    // What the game shows about the character now, checked against the plot just read. Unresolved checks pass to the
    // game, which refuses an entry it does not allow (LogMessage 3497, 6099, 6100); known blockers stop here.
    private EntryFailure VerifyEligibility(PlacardReading placard)
    {
        Move(EntryStep.VerifyEligibility);
        if (CharacterContextReader.Read() is not { } context || context.ContentId != session.Settings.ContentId)
        {
            return EntryFailure.CharacterChanged;
        }

        var position = services.Housing.CurrentPosition(DateTime.UtcNow);
        var profile = services.Housing.Profile(context, reading, position);
        var character = EligibilityRules.EvaluateCharacter(profile, session.Settings.Buyer, position.Cycle, collectRefundFirst: false);
        if ((character.Blockers & EligibilityReason.AlreadyEnteredThisCycle) != 0)
        {
            return EntryFailure.AlreadyEntered;
        }

        if ((character.Blockers & EligibilityReason.OutstandingRefundOrProperty) != 0)
        {
            return profile.Outstanding == OutstandingSettlement.UnclaimedWin ? EntryFailure.UnclaimedWin : EntryFailure.CollectRefundFirst;
        }

        if (character.Blockers != EligibilityReason.None)
        {
            return EntryFailure.CharacterIneligible;
        }

        var candidate = new EntryCandidate(session.Target, PlacardObservations.BuyersOf(placard.Sale) ?? PlotBuyers.Unknown, PlotAvailability.LotteryEntry, session.ExpectedDeposit(session.Target));
        var plot = EligibilityRules.EvaluatePlot(profile, session.Settings.Buyer, candidate, session.Budget, OtherCommitments());
        Diag($"Entry run {session.RunId}: eligibility for {session.Target}: character {character.Verdict} (unresolved {character.Unresolved}), plot blockers {plot.Blockers}, unresolved {plot.Unresolved}.");
        if ((plot.Blockers & EligibilityReason.BuyerTypeMismatch) != 0)
        {
            return EntryFailure.Ineligible;
        }

        if ((plot.Blockers & (EligibilityReason.DepositOverCap | EligibilityReason.InsufficientGil)) != 0)
        {
            return EntryFailure.OverBudget;
        }

        return plot.Blockers == EligibilityReason.None ? EntryFailure.None : EntryFailure.CharacterIneligible;
    }
}
