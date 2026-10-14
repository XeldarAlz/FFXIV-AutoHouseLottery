using AutoHouseLottery.Core.Shared;

namespace AutoHouseLottery.Core.Eligibility.Model;

internal static class BudgetRules
{
    public static BudgetCheck Check(in CharacterBudget budget, in DepositLedger ledger, long? deposit, long? observedGil)
    {
        if (deposit is not { } amount || amount <= 0)
        {
            return BudgetCheck.DepositUnknown;
        }

        if (!Gil.TryAdd(ledger.GrossSubmitted, ledger.PendingReservations, out var committed) || !Gil.TryAdd(committed, amount, out var authorized))
        {
            return BudgetCheck.Overflow;
        }

        if (authorized > budget.DepositCap)
        {
            return BudgetCheck.OverDepositCap;
        }

        if (observedGil is not { } gil)
        {
            return BudgetCheck.BalanceUnknown;
        }

        if (!Gil.TryAdd(amount, ledger.PendingReservations, out var outgoing)
            || !Gil.TryAdd(outgoing, budget.TravelAllowance, out outgoing)
            || !Gil.TryAdd(outgoing, budget.GilReserve, out var required))
        {
            return BudgetCheck.Overflow;
        }

        return required <= gil ? BudgetCheck.Fits : BudgetCheck.BelowReserve;
    }
}
