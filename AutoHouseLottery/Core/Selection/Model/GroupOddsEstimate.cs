using AutoHouseLottery.Core.Eligibility.Model;

namespace AutoHouseLottery.Core.Selection.Model;

internal readonly record struct GroupOddsEstimate(OwnershipGroup Group, OddsEstimate? Estimate);
