using System.Collections.Immutable;
using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Selection.Model;

internal readonly record struct RunAssignment(
    ulong ContentId,
    OwnershipGroup Group,
    PlacementMode Placement,
    ImmutableArray<PlotKey> TargetOrder,
    long MaximumDeposit,
    bool CollectsRefundFirst,
    EligibilityResult Eligibility);
