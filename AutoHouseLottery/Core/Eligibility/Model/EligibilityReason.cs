namespace AutoHouseLottery.Core.Eligibility.Model;

[Flags]
internal enum EligibilityReason : ushort
{
    None = 0,
    AlreadyEnteredThisCycle = 1 << 0,
    AccountOwnsLandOnWorld = 1 << 1,
    OutstandingRefundOrProperty = 1 << 2,
    InsufficientGil = 1 << 3,
    DepositOverCap = 1 << 4,
    WrongHomeWorld = 1 << 5,
    BuyerTypeMismatch = 1 << 6,
    BuyerRequirementsNotMet = 1 << 7,
    PlotUnavailable = 1 << 8,
    ProfileUnavailable = 1 << 9,
    PlotNeedsVerification = 1 << 10,
    GroupOnAnotherPlot = 1 << 11,
    UnsupportedRegion = 1 << 12,
}
