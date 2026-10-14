using System.Collections.Frozen;
using System.Collections.Immutable;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Saved.Model;

internal sealed record NoticePlan(
    FrozenDictionary<Guid, ImmutableArray<PlotKey>> Matches,
    ImmutableArray<Notice> Announce,
    ImmutableArray<Notice> Silent,
    ImmutableArray<Guid> ReadySources,
    ImmutableArray<PlotKey> ReadyPlots);
