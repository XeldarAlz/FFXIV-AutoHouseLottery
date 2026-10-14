using System.Collections.Immutable;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Saved.Model;

internal sealed record SavedDocument(
    ImmutableArray<SavedSearch> Searches,
    ImmutableArray<SavedList> Lists,
    ImmutableArray<KeyValuePair<string, DateTime>> Delivered,
    ImmutableArray<NoticeRecord> Recent,
    ImmutableArray<Guid> PendingSources,
    ImmutableArray<PlotKey> PendingPlots,
    int Skipped);
