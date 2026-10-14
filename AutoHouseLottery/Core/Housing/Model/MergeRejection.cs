namespace AutoHouseLottery.Core.Housing.Model;

internal enum MergeRejection : byte
{
    None,
    InvalidAddress,
    IdentityMismatch,
    UnknownSource,
    InvalidTimestamp,
    FutureTimestamp,
    InvalidPrice,
    NegativeEntryCount,
    MissingCycle,
    UnknownValue,
    ContradictoryFields,
}
