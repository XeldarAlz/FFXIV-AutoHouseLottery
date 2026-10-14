using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Saved.Model;

internal sealed record SavedDocument(
    ImmutableArray<SavedSearch> Searches,
    ImmutableArray<SavedList> Lists,
    ImmutableArray<KeyValuePair<string, DateTime>> Delivered,
    ImmutableArray<NoticeRecord> Recent,
    int Skipped);
