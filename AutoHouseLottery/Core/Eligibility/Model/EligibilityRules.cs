using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Eligibility.Model;

internal static class EligibilityRules
{
    public static EligibilityResult EvaluateCharacter(in CharacterProfile character, BuyerType buyer, CycleKey cycle, bool collectRefundFirst)
    {
        var blockers = EligibilityReason.None;
        var unresolved = EligibilityReason.None;
        switch (character.Participation.StateIn(cycle))
        {
            case EntrySlotState.Open:
                break;
            case EntrySlotState.Entered:
            case EntrySlotState.Unresolved:
                blockers |= EligibilityReason.AlreadyEnteredThisCycle;
                break;
            default:
                unresolved |= EligibilityReason.AlreadyEnteredThisCycle;
                break;
        }

        switch (character.Outstanding)
        {
            case OutstandingSettlement.None:
            case OutstandingSettlement.RefundDue when collectRefundFirst:
                break;
            case OutstandingSettlement.RefundDue:
            case OutstandingSettlement.UnclaimedWin:
                blockers |= EligibilityReason.OutstandingRefundOrProperty;
                break;
            default:
                unresolved |= EligibilityReason.OutstandingRefundOrProperty;
                break;
        }

        Accumulate(MeetsBuyerRequirements(character, buyer), EligibilityReason.BuyerRequirementsNotMet, ref blockers, ref unresolved);
        if (buyer == BuyerType.Private)
        {
            var landFree = character.AccountOwnsLandOnHomeWorld is { } owns ? !owns : (bool?)null;
            Accumulate(landFree, EligibilityReason.AccountOwnsLandOnWorld, ref blockers, ref unresolved);
        }

        return new EligibilityResult(blockers, unresolved, Earliest(character.Participation.ObservedUtc, character.FactsObservedUtc));
    }

    public static EligibilityResult EvaluatePlot(
        in CharacterProfile character,
        BuyerType buyer,
        in EntryCandidate candidate,
        in CharacterBudget budget,
        in DepositLedger ledger)
    {
        var blockers = EligibilityReason.None;
        var unresolved = EligibilityReason.None;
        if (candidate.Plot.Region != character.Region || candidate.Plot.WorldId != character.HomeWorldId)
        {
            blockers |= EligibilityReason.WrongHomeWorld;
        }

        Accumulate(Allows(candidate.Buyers, buyer), EligibilityReason.BuyerTypeMismatch, ref blockers, ref unresolved);
        if (candidate.NeedsVerification)
        {
            blockers |= EligibilityReason.PlotNeedsVerification;
        }

        switch (candidate.Availability)
        {
            case PlotAvailability.LotteryEntry:
                break;
            case PlotAvailability.Unknown:
            case PlotAvailability.NotReported:
                unresolved |= EligibilityReason.PlotUnavailable;
                break;
            default:
                blockers |= EligibilityReason.PlotUnavailable;
                break;
        }

        switch (BudgetRules.Check(budget, ledger, candidate.Deposit, character.ObservedGil))
        {
            case BudgetCheck.Fits:
                break;
            case BudgetCheck.DepositUnknown:
                unresolved |= EligibilityReason.DepositOverCap;
                break;
            case BudgetCheck.BalanceUnknown:
                unresolved |= EligibilityReason.InsufficientGil;
                break;
            case BudgetCheck.BelowReserve:
                blockers |= EligibilityReason.InsufficientGil;
                break;
            default:
                blockers |= EligibilityReason.DepositOverCap;
                break;
        }

        return new EligibilityResult(blockers, unresolved, character.GilObservedUtc);
    }

    public static bool? Allows(PlotBuyers buyers, BuyerType buyer) => buyers switch
    {
        PlotBuyers.Both => buyer != BuyerType.Unknown,
        PlotBuyers.Private => buyer == BuyerType.Private,
        PlotBuyers.FreeCompany => buyer == BuyerType.FreeCompany,
        _ => null,
    };

    private static bool? MeetsBuyerRequirements(in CharacterProfile character, BuyerType buyer)
    {
        return buyer switch
        {
            BuyerType.Private => character.PrivateBuyerQualified,
            BuyerType.FreeCompany when character.FreeCompanyId == CharacterProfile.NoFreeCompany => false,
            BuyerType.FreeCompany when character.FreeCompanyId is null => null,
            BuyerType.FreeCompany => character.FreeCompanyBuyerQualified,
            _ => false,
        };
    }

    private static DateTime? Earliest(DateTime? first, DateTime? second)
    {
        if (first is { } left && second is { } right)
        {
            return left < right ? left : right;
        }

        return first ?? second;
    }

    private static void Accumulate(bool? passes, EligibilityReason reason, ref EligibilityReason blockers, ref EligibilityReason unresolved)
    {
        if (passes is null)
        {
            unresolved |= reason;
            return;
        }

        if (!passes.Value)
        {
            blockers |= reason;
        }
    }
}
