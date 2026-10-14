using System.Collections.Immutable;
using AutoHouseLottery.Core.Selection.Model;

namespace AutoHouseLottery.Core.Find.Model;

internal static class BestMatchOrder
{
    public const int Shown = RunPolicy.MaximumInspections;

    // The plots the planner would try for this character, in order; before it can plan, the plain ranking.
    public static ImmutableArray<RankedPlot> For(RunPreview? preview, ImmutableArray<RankedPlot> ranking)
    {
        if (preview is not { PolicyError: RunPolicyError.None, Assignments.Length: > 0 } planned)
        {
            return ranking.Length <= Shown ? ranking : ranking[..Shown];
        }

        var targets = planned.Assignments[0].TargetOrder;
        var ordered = ImmutableArray.CreateBuilder<RankedPlot>(Math.Min(targets.Length, Shown));
        for (var index = 0; index < targets.Length && ordered.Count < Shown; index++)
        {
            for (var rankIndex = 0; rankIndex < planned.Ranking.Length; rankIndex++)
            {
                if (planned.Ranking[rankIndex].Key == targets[index])
                {
                    ordered.Add(planned.Ranking[rankIndex]);
                    break;
                }
            }
        }

        return ordered.ToImmutable();
    }
}
