using AutoHouseLottery.Core.Eligibility.Model;

namespace AutoHouseLottery.Core.Tasks.Model;

// What the entry confirmation and the character show at the last moment before Confirm. ExpectedDeposit is the amount
// the run was started with: the exact plot's bound, or the ranked deposit of a best match plot when one was known.
// Ledger holds what the run has committed besides this entry, so its own reservation is not counted twice.
internal readonly record struct ConfirmationFacts(
    bool Identified,
    long? ShownDeposit,
    RunMode Mode,
    long? ExpectedDeposit,
    CharacterBudget Budget,
    DepositLedger Ledger,
    long? LiveGil);

internal static class ConfirmationRules
{
    // An exact plot is entered only for the amount it was started with; a changed amount goes back to the player
    // either way. A best match plot may show an amount the listing did not, but not a different one, and never more
    // than the character's cap or than its gil allows after the reserve.
    public static EntryFailure Check(in ConfirmationFacts facts)
    {
        if (!facts.Identified)
        {
            return EntryFailure.ConfirmationNotShown;
        }

        if (facts.ShownDeposit is not { } deposit)
        {
            return EntryFailure.DepositUnreadable;
        }

        if (facts.ExpectedDeposit is { } expected && expected != deposit)
        {
            return EntryFailure.ChangedPrice;
        }

        if (facts.Mode == RunMode.Exact && facts.ExpectedDeposit is null)
        {
            return EntryFailure.ChangedPrice;
        }

        return BudgetRules.Check(facts.Budget, facts.Ledger, deposit, facts.LiveGil) switch
        {
            BudgetCheck.Fits => EntryFailure.None,
            BudgetCheck.BalanceUnknown => EntryFailure.GilUnknown,
            _ => EntryFailure.OverBudget,
        };
    }
}
