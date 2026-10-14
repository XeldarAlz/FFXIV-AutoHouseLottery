using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Selection.Model;

internal static class RunPolicyRules
{
    public static RunPolicyError Validate(RunPolicy policy)
    {
        if (!policy.Cycle.Cycle.IsValid)
        {
            return RunPolicyError.MissingCycle;
        }

        if (policy.Characters.IsDefaultOrEmpty)
        {
            return RunPolicyError.NoCharacters;
        }

        if (policy.TravelAllowance < 0)
        {
            return RunPolicyError.NegativeTravelAllowance;
        }

        if (policy.MaxInspections < 1 || policy.MaxInspections > RunPolicy.MaximumInspections)
        {
            return RunPolicyError.InspectionLimitOutOfRange;
        }

        var characterError = ValidateCharacters(policy);
        if (characterError != RunPolicyError.None)
        {
            return characterError;
        }

        if (policy.Exact is null && policy.Shortlist is null)
        {
            return RunPolicyError.MissingTarget;
        }

        if (policy.Exact is not null && policy.Shortlist is not null)
        {
            return RunPolicyError.AmbiguousTarget;
        }

        if (policy.Exact is { } exact && (!exact.Plot.IsValid || exact.Deposit <= 0))
        {
            return RunPolicyError.InvalidExactTarget;
        }

        return RunPolicyError.None;
    }

    private static RunPolicyError ValidateCharacters(RunPolicy policy)
    {
        var characters = policy.Characters;
        for (var index = 0; index < characters.Length; index++)
        {
            var settings = characters[index];
            if (settings.Buyer == BuyerType.Unknown)
            {
                return RunPolicyError.MissingBuyerType;
            }

            if (settings.DepositCap <= 0)
            {
                return RunPolicyError.NonPositiveDepositCap;
            }

            if (settings.GilReserve < 0)
            {
                return RunPolicyError.NegativeReserve;
            }

            var targetError = ValidateTarget(policy, settings.Target);
            if (targetError != RunPolicyError.None)
            {
                return targetError;
            }

            for (var earlierIndex = 0; earlierIndex < index; earlierIndex++)
            {
                if (characters[earlierIndex].ContentId == settings.ContentId)
                {
                    return RunPolicyError.DuplicateCharacter;
                }
            }
        }

        return RunPolicyError.None;
    }

    // A character's own target is a plot of the frozen shortlist; an exact-plot policy already names the one plot.
    private static RunPolicyError ValidateTarget(RunPolicy policy, PlotKey? target)
    {
        if (target is not { } plot)
        {
            return RunPolicyError.None;
        }

        if (policy.Exact is not null)
        {
            return RunPolicyError.AmbiguousTarget;
        }

        return plot.IsValid && policy.Shortlist is { } shortlist && shortlist.Plots.IndexOf(plot) >= 0 ? RunPolicyError.None : RunPolicyError.InvalidExactTarget;
    }
}
