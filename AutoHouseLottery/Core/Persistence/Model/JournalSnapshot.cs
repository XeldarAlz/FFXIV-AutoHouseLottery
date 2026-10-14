using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Persistence.Model;

internal readonly record struct JournalSnapshot(
    ImmutableArray<ActionAttempt> Attempts,
    ImmutableArray<EntryReceipt> Receipts,
    ImmutableArray<EntrySettlement> Settlements);
