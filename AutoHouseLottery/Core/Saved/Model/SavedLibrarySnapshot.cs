using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Saved.Model;

internal readonly record struct SavedLibrarySnapshot(int Revision, ImmutableArray<SavedSearch> Searches, ImmutableArray<SavedList> Lists);
