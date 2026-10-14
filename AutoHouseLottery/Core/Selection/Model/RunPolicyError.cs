namespace AutoHouseLottery.Core.Selection.Model;

internal enum RunPolicyError : byte
{
    None,
    MissingCycle,
    NoCharacters,
    DuplicateCharacter,
    MissingBuyerType,
    NonPositiveDepositCap,
    NegativeReserve,
    NegativeTravelAllowance,
    InspectionLimitOutOfRange,
    MissingTarget,
    AmbiguousTarget,
    InvalidExactTarget,
}
