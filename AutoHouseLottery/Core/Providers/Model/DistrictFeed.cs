using System.Collections.Immutable;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Providers.Model;

internal sealed record DistrictFeed(CoverageSnapshot Coverage, ImmutableArray<PlotObservation> Observations, int RejectedRows);
