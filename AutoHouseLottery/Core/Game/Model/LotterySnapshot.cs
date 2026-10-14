using System.Buffers.Binary;
using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Game.Model;

// Unloaded: nothing readable says whether there is an entry (no record, or no lottery text on screen). LoadedNone: the
// record is loaded and says this character has no entry; only the validated record can say that. Invalid: the record
// is outside the layout's known values, so nothing is trusted.
internal enum LotterySnapshotState : byte
{
    NoCharacter,
    ManagerMissing,
    Unloaded,
    LoadedNone,
    Active,
    Invalid,
}

// Record: the record behind HousingManager + 0xB0, read only on a build validated in game. TimersText: the lottery
// lines the Timers window shows, read from the window and matched by their Addon rows.
internal enum LotterySource : byte
{
    Record,
    TimersText,
}

// NotHomeWorld: the Timers window only fetches the record on the home world (Addon row 8841).
internal enum LotteryPassOutcome : byte
{
    Loaded,
    NotLoaded,
    NoCharacter,
    NotHomeWorld,
    PlayerBusy,
    CharacterChanged,
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

// Ward and plot are zero-based. From the record every number is set and BuyerType and Phase are null only for a byte
// outside the known values. From the Timers text the address and the entry type stay as the window shows them.
internal readonly record struct LotteryEntry(
    uint? TerritoryTypeId,
    int? WardIndex,
    int? PlotIndex,
    uint? LotteryNumber,
    LotteryBuyerType? BuyerType,
    LotteryPhase? Phase,
    byte? RawBuyerType,
    byte? RawPhase,
    string? AddressText,
    string? EntryTypeText);

internal readonly record struct LotterySnapshot(LotterySnapshotState State, LotterySource Source, ulong ContentId, LotteryEntry? Entry, DateTime ReadAtUtc)
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
            rawPhase,
            null,
            null);
        return LotterySnapshotState.Active;
    }
}

// The record pointer is read only on game builds the maintainer validated in game, and only when it looks like a heap
// address. Every other build reads the Timers window's text instead.
internal static class LotteryRecordAccess
{
    // Game versions (Framework.GameVersionString) on which HousingManager + 0xB0 and the record layout were checked in
    // game. Empty until that check is done.
    public static readonly ImmutableArray<string> ValidatedGameVersions = [];

    private const ulong LowestUserAddress = 0x10000;
    private const ulong HighestUserAddress = 0x7FFF_FFFE_FFFF;
    private const ulong PointerAlignment = 8;

    public static bool IsValidated(string gameVersion)
    {
        for (var versionIndex = 0; versionIndex < ValidatedGameVersions.Length; versionIndex++)
        {
            if (string.Equals(ValidatedGameVersions[versionIndex], gameVersion, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsPlausiblePointer(ulong address)
        => address is >= LowestUserAddress and <= HighestUserAddress && address % PointerAlignment == 0;
}

// The lottery lines of the Timers window, matched by their Addon rows: 8844 or 8845 for the phase, 8848 for the lottery
// number, 8847 for the address and 8849 for the entry type. Only a lottery number makes an entry; text that is absent
// never means "no entry".
internal static class LotteryWindowText
{
    private const ulong DecimalBase = 10;

    public static LotterySnapshotState Parse(ReadOnlySpan<string> shown, in LotteryTextPatterns patterns, out LotteryEntry entry)
    {
        entry = default;
        LotteryPhase? phase = null;
        uint? lotteryNumber = null;
        string? addressText = null;
        string? entryTypeText = null;
        for (var textIndex = 0; textIndex < shown.Length; textIndex++)
        {
            var text = shown[textIndex];
            if (patterns.EntryPeriod.Matches(text))
            {
                phase = LotteryPhase.EntryPeriod;
            }
            else if (patterns.ResultsPeriod.Matches(text))
            {
                phase = LotteryPhase.ResultsPeriod;
            }
            else if (patterns.Number.TryCapture(text, out var numberGaps) && FirstNumber(numberGaps) is { } number)
            {
                lotteryNumber = number;
            }
            else if (patterns.Address.Matches(text))
            {
                addressText = text;
            }
            else if (patterns.EntryType.Matches(text))
            {
                entryTypeText = text;
            }
        }

        if (lotteryNumber is null)
        {
            return LotterySnapshotState.Unloaded;
        }

        entry = new LotteryEntry(null, null, null, lotteryNumber, null, phase, null, null, addressText, entryTypeText);
        return LotterySnapshotState.Active;
    }

    // The first run of digits in the captured macros, reading through digit grouping marks.
    private static uint? FirstNumber(ImmutableArray<string> gaps)
    {
        for (var gapIndex = 0; gapIndex < gaps.Length; gapIndex++)
        {
            var gap = gaps[gapIndex];
            ulong value = 0;
            var digits = 0;
            for (var characterIndex = 0; characterIndex < gap.Length; characterIndex++)
            {
                var character = gap[characterIndex];
                if (char.IsAsciiDigit(character))
                {
                    value = (value * DecimalBase) + (ulong)(character - '0');
                    digits++;
                    if (value > uint.MaxValue)
                    {
                        return null;
                    }

                    continue;
                }

                if (digits > 0 && !IsGroupingMark(character))
                {
                    break;
                }
            }

            if (digits > 0)
            {
                return (uint)value;
            }
        }

        return null;
    }

    private static bool IsGroupingMark(char character) => character is ',' or '.' or ' ' or ' ' or ' ';
}

internal readonly record struct LotteryTextPatterns(
    SheetTextPattern EntryPeriod,
    SheetTextPattern ResultsPeriod,
    SheetTextPattern Number,
    SheetTextPattern Address,
    SheetTextPattern EntryType);
