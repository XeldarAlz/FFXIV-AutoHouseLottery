namespace AutoHouseLottery.Core.Persistence.Model;

internal enum JournalWrite : byte
{
    Written,
    AlreadyRecorded,
    SlotTaken,
    UnknownRecord,
    IllegalTransition,
    Invalid,
    WriteFailed,
    Faulted,
    AmountMismatch,
    WrittenAmountDiffers,
}
