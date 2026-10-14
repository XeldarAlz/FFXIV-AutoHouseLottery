namespace AutoHouseLottery.Core.Persistence.Model;

internal enum JournalRecordKind : byte { Header, Attempt, Receipt, Settlement, Confirmation, Import }
