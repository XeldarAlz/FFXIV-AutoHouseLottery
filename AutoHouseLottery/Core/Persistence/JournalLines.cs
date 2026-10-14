using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Persistence;

internal readonly record struct JournalLines(byte[] Content, ImmutableArray<Range> Lines, long CompleteLength, bool TruncatedTail);
