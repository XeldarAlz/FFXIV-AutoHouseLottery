using System.Collections.Immutable;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Selection.Model;

internal sealed record RunPolicy
{
    public const int MaximumInspections = 5;

    public required int Revision { get; init; }

    public required CyclePosition Cycle { get; init; }

    public required ImmutableArray<CharacterRunSettings> Characters { get; init; }

    public required long TravelAllowance { get; init; }

    public ExactTarget? Exact { get; init; }

    public ShortlistSnapshot? Shortlist { get; init; }

    public PlacementChoice Placement { get; init; }

    public bool MultipleWinRiskAccepted { get; init; }

    public int MaxInspections { get; init; } = MaximumInspections;

    public bool CollectRefundsFirst { get; init; } = true;
}
