using System.Buffers.Binary;

namespace AutoHouseLottery.Core.Game.Model;

// Unloaded: the game holds no lottery record yet, which the Timers window fetches. LoadedNone: the record is loaded and
// says this character has no entry. Invalid: loaded but outside the layout's known values, so nothing is trusted.
internal enum LotterySnapshotState : byte
{
    NoCharacter,
    ManagerMissing,
    Unloaded,
    LoadedNone,
    Active,
    Invalid,
}

// NotHomeWorld: the Timers window only fetches the record on the home world (Addon row 8841).
internal enum LotteryPassOutcome : byte
{
    Loaded,
    NotLoaded,
    NoCharacter,
    NotHomeWorld,
    PlayerBusy,
    Cancelled,
}

internal enum LotteryBuyerType : byte
{
    FreeCompany = 1,
    Private = 2,
}

internal enum LotteryPhase : byte
{
    EntryPeriod = 1,
    ResultsPeriod = 2,
}

// Ward and plot are zero-based. BuyerType and Phase are null for a byte outside the known values; the raw bytes stay.
internal readonly record struct LotteryEntry(
    uint TerritoryTypeId,
    int WardIndex,
    int PlotIndex,
    uint LotteryNumber,
    LotteryBuyerType? BuyerType,
    LotteryPhase? Phase,
    byte RawBuyerType,
    byte RawPhase);

internal readonly record struct LotterySnapshot(LotterySnapshotState State, ulong ContentId, LotteryEntry? Entry, DateTime ReadAtUtc)
{
    public bool Loaded => State is LotterySnapshotState.LoadedNone or LotterySnapshotState.Active;
}

// The 16-byte personal lottery record HouseHunter reads through a pointer at HousingManager + 0xB0: plot +0x00,
// ward +0x02, territory +0x04, lottery number +0x08, buyer type +0x0C, phase +0x0D. HouseHunter treats a first quadword
// of 0, -1 or -2 as "not participating".
internal static class LotteryEntryLayout
{
    public const int Length = 16;

    private const int PlotOffset = 0x00;
    private const int WardOffset = 0x02;
    private const int TerritoryOffset = 0x04;
    private const int NumberOffset = 0x08;
    private const int BuyerTypeOffset = 0x0C;
    private const int PhaseOffset = 0x0D;
    private const ulong NoEntry = 0;
    private const ulong NoEntryMinusOne = ulong.MaxValue;
    private const ulong NoEntryMinusTwo = ulong.MaxValue - 1;

    public static LotterySnapshotState Parse(ReadOnlySpan<byte> record, out LotteryEntry entry)
    {
        entry = default;
        var head = BinaryPrimitives.ReadUInt64LittleEndian(record);
        if (head is NoEntry or NoEntryMinusOne or NoEntryMinusTwo)
        {
            return LotterySnapshotState.LoadedNone;
        }

        var plotIndex = record[PlotOffset];
        var wardIndex = record[WardOffset];
        var territoryTypeId = BinaryPrimitives.ReadUInt16LittleEndian(record[TerritoryOffset..]);
        if (!ResidentialDistricts.IsPlotIndex(plotIndex) || !ResidentialDistricts.IsWardIndex(wardIndex) || !ResidentialDistricts.IsResidential(territoryTypeId))
        {
            return LotterySnapshotState.Invalid;
        }

        var rawBuyerType = record[BuyerTypeOffset];
        var rawPhase = record[PhaseOffset];
        entry = new LotteryEntry(
            territoryTypeId,
            wardIndex,
            plotIndex,
            BinaryPrimitives.ReadUInt32LittleEndian(record[NumberOffset..]),
            rawBuyerType is (byte)LotteryBuyerType.FreeCompany or (byte)LotteryBuyerType.Private ? (LotteryBuyerType)rawBuyerType : null,
            rawPhase is (byte)LotteryPhase.EntryPeriod or (byte)LotteryPhase.ResultsPeriod ? (LotteryPhase)rawPhase : null,
            rawBuyerType,
            rawPhase);
        return LotterySnapshotState.Active;
    }
}
