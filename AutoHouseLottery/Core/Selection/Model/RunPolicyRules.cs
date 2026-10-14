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
}
