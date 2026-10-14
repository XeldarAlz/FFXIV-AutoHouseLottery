using AutoHouseLottery.Core.Persistence.Model;

namespace AutoHouseLottery.Core.Persistence;

internal readonly record struct JournalOpenResult(
    JournalOpenStatus Status,
    ActionJournal? Journal,
    int RecoveredAttempts,
    bool TruncatedTail,
    string? BackupPath);
