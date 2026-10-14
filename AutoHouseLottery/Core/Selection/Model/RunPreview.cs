using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Selection.Model;

internal sealed record RunPreview
{
    public required int PolicyRevision { get; init; }

    public required RunPolicyError PolicyError { get; init; }

    public required int PlotCount { get; init; }

    public required int SelectedCharacterCount { get; init; }

    public required ImmutableArray<RankedPlot> Ranking { get; init; }

    public required ImmutableArray<RunAssignment> Assignments { get; init; }

    public required ImmutableArray<CharacterExclusion> Exclusions { get; init; }

    public required long TotalMaximumDeposit { get; init; }

    public required OddsEstimate? Odds { get; init; }

    public required ImmutableArray<GroupOddsEstimate> GroupOdds { get; init; }

    public required RunBlocker Blockers { get; init; }

    public int MaxNewEntries => Assignments.Length;

    public bool CanRun => Blockers == RunBlocker.None;
}
