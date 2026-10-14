namespace AutoHouseLottery.Core.Selection.Model;

[Flags]
internal enum RunBlocker : byte
{
    None = 0,
    InvalidPolicy = 1 << 0,
    CycleNotVerified = 1 << 1,
    EntryPeriodClosed = 1 << 2,
    NoAssignment = 1 << 3,
    MultipleWinsNeedConfirmation = 1 << 4,
    DepositTotalOverflow = 1 << 5,
}
