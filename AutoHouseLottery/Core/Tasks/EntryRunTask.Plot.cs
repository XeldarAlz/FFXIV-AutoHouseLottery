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
    private async Task<EntryFailure> TravelToTarget()
    {
        Move(EntryStep.Travel);
        var destination = HousingDestination.Of(session.Target);
        var label = $"Entry run {session.RunId}";
        var failure = await TravelToPlot(destination, session.Settings.ContentId, session.ManualArrival, label, () => Move(EntryStep.Travel));
        if (failure != VisitFailure.None)
        {
            return FromVisit(failure);
        }

        Move(EntryStep.VerifyArrival);
        return FromVisit(VerifyArrival(destination, label));
    }

    private async Task<EntryFailure> OpenPlacard()
    {
        Move(EntryStep.OpenPlacard);
        return FromVisit(await OpenPlacardAt(HousingDestination.Of(session.Target), services.Observers, session.ManualArrival, $"Entry run {session.RunId}"));
    }

    private static EntryFailure FromVisit(VisitFailure failure) => failure switch
    {
        VisitFailure.None => EntryFailure.None,
        VisitFailure.CharacterChanged => EntryFailure.CharacterChanged,
        VisitFailure.TravelUnavailable => EntryFailure.TravelUnavailable,
        VisitFailure.DependencyBusy => EntryFailure.DependencyBusy,
        VisitFailure.WrongDestination => EntryFailure.WrongDestination,
        VisitFailure.PlacardNotFound => EntryFailure.PlacardNotFound,
        VisitFailure.PlacardNotOpened => EntryFailure.PlacardNotOpened,
        _ => EntryFailure.TravelFailed,
    };

    // The placard's sources must agree on its address before anything it shows is believed.
    private async Task<(EntryFailure Failure, PlacardReading? Reading)> ReadLivePlot()
    {
        Move(EntryStep.ReadLivePlot);
        var (read, proved) = await ReadPlacardSettled(services.Observers, services.Housing);
        if (read is not { } placard)
        {
            return (EntryFailure.PlacardNotOpened, null);
        }

        session.LiveCycle = proved?.Cycle;
        var facts = new LivePlotFacts(
            PlacardIdentity.Verify(HousingDestination.Of(session.Target), placard.Address),
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
