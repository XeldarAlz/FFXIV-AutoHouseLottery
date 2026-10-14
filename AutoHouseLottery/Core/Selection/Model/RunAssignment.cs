using System.Collections.Immutable;
using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Selection.Model;

// Exact: the character's own chosen plot, which no fallback and no other member's confirmed entry ever replaces.
internal readonly record struct RunAssignment(
    ulong ContentId,
    OwnershipGroup Group,
    PlacementMode Placement,
    ImmutableArray<PlotKey> TargetOrder,
    long MaximumDeposit,
    bool CollectsRefundFirst,
    EligibilityResult Eligibility)
{
    public bool Exact { get; init; }
}
