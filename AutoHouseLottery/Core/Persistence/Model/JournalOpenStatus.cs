namespace AutoHouseLottery.Core.Persistence.Model;

internal enum JournalOpenStatus : byte { Created, Opened, Migrated, Corrupt, NewerSchema, MigrationFailed, ReadFailed, Locked, RecoveryFailed }
