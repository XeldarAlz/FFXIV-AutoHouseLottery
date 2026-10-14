using AutoHouseLottery.Core.Game.Model;
using System.Buffers.Binary;

namespace AutoHouseLottery.Tests.Game;

public sealed class LotteryEntryLayoutTests
{
    private const ushort Mist = 339;
    private const ushort Shirogane = 641;
    private const ushort Limsa = 129;

    [Theory]
    [InlineData(0ul)]
    [InlineData(ulong.MaxValue)]
    [InlineData(ulong.MaxValue - 1)]
    public void TheNoEntrySentinelsReadAsLoadedWithNoEntry(ulong head)
    {
        var record = new byte[LotteryEntryLayout.Length];
        BinaryPrimitives.WriteUInt64LittleEndian(record, head);
        Assert.Equal(LotterySnapshotState.LoadedNone, LotteryEntryLayout.Parse(record, out _));
    }

    [Fact]
    public void ReadsAnActivePrivateEntry()
    {
        var state = LotteryEntryLayout.Parse(Record(plot: 4, ward: 11, Mist, number: 1234, buyer: 2, phase: 1), out var entry);
        Assert.Equal(LotterySnapshotState.Active, state);
        Assert.Equal(new LotteryEntry(Mist, 11, 4, 1234, LotteryBuyerType.Private, LotteryPhase.EntryPeriod, 2, 1, null, null), entry);
    }

    [Fact]
    public void ReadsAFreeCompanyEntryInTheResultsPeriod()
    {
        LotteryEntryLayout.Parse(Record(plot: 59, ward: 29, Shirogane, number: 7, buyer: 1, phase: 2), out var entry);
        Assert.Equal(LotteryBuyerType.FreeCompany, entry.BuyerType);
        Assert.Equal(LotteryPhase.ResultsPeriod, entry.Phase);
    }

    [Fact]
    public void UnknownBuyerAndPhaseBytesStayUnknown()
    {
        var state = LotteryEntryLayout.Parse(Record(plot: 0, ward: 0, Mist, number: 1, buyer: 0, phase: 9), out var entry);
        Assert.Equal(LotterySnapshotState.Active, state);
        Assert.Null(entry.BuyerType);
        Assert.Null(entry.Phase);
        Assert.Equal((byte)9, entry.RawPhase);
    }

    [Theory]
    [InlineData(60, 0, Mist)]
    [InlineData(0, 30, Mist)]
    [InlineData(0, 0, Limsa)]
    public void ValuesOutsideTheLayoutAreInvalidRatherThanAnEntry(byte plot, byte ward, ushort territory)
    {
        Assert.Equal(LotterySnapshotState.Invalid, LotteryEntryLayout.Parse(Record(plot, ward, territory, number: 1, buyer: 2, phase: 1), out _));
    }

    [Fact]
    public void OnlyALoadedRecordCountsAsLoaded()
    {
        var now = DateTime.UtcNow;
        Assert.True(new LotterySnapshot(LotterySnapshotState.LoadedNone, LotterySource.Record, 1, null, now).Loaded);
        Assert.True(new LotterySnapshot(LotterySnapshotState.Active, LotterySource.Record, 1, null, now).Loaded);
        Assert.False(new LotterySnapshot(LotterySnapshotState.Unloaded, LotterySource.Record, 1, null, now).Loaded);
        Assert.False(new LotterySnapshot(LotterySnapshotState.Invalid, LotterySource.Record, 1, null, now).Loaded);
        Assert.False(new LotterySnapshot(LotterySnapshotState.NoCharacter, LotterySource.TimersText, 0, null, now).Loaded);
    }

    [Fact]
    public void NoBuildIsValidatedForTheRecordUntilTheMaintainerAddsOne()
    {
        Assert.Empty(LotteryRecordAccess.ValidatedGameVersions);
        Assert.False(LotteryRecordAccess.IsValidated("2026.10.01.0000.0000"));
        Assert.False(LotteryRecordAccess.IsValidated(string.Empty));
    }

    [Theory]
    [InlineData(0x0000_01F2_3456_7890ul, true)]
    [InlineData(0x0000_01F2_3456_7891ul, false)]
    [InlineData(0ul, false)]
    [InlineData(0x8000ul, false)]
    [InlineData(0xFFFF_8000_0000_0000ul, false)]
    [InlineData(ulong.MaxValue, false)]
    public void OnlyAnAlignedUserSpaceAddressIsAPlausibleRecord(ulong address, bool expected)
    {
        Assert.Equal(expected, LotteryRecordAccess.IsPlausiblePointer(address));
    }

    [Fact]
    public void TheTimersTextGivesAnEntryOnlyWithALotteryNumber()
    {
        string[] shown = ["Results period in progress.", "Current Entry", "Plot 5, 12th Ward, Mist (Small)", "Lottery Number: 1,234", "Type of Entry: Private Buyer"];
        var state = LotteryWindowText.Parse(shown, Patterns(), out var entry);
        Assert.Equal(LotterySnapshotState.Active, state);
        Assert.Equal(1234u, entry.LotteryNumber);
        Assert.Equal(LotteryPhase.ResultsPeriod, entry.Phase);
        Assert.Equal("Plot 5, 12th Ward, Mist (Small)", entry.AddressText);
        Assert.Equal("Type of Entry: Private Buyer", entry.EntryTypeText);
        Assert.Null(entry.WardIndex);
        Assert.Null(entry.BuyerType);
    }

    [Fact]
    public void TimersTextWithoutALotteryNumberNeverMeansNoEntry()
    {
        string[] shown = ["Entry period in progress.", "Housing Lottery Status"];
        Assert.Equal(LotterySnapshotState.Unloaded, LotteryWindowText.Parse(shown, Patterns(), out _));
        Assert.Equal(LotterySnapshotState.Unloaded, LotteryWindowText.Parse([], Patterns(), out _));
    }

    private static LotteryTextPatterns Patterns()
        => new(
            SheetTextPattern.FromTokens([SheetTextToken.Literal("Entry period in progress.")]),
            SheetTextPattern.FromTokens([SheetTextToken.Literal("Results period in progress.")]),
            SheetTextPattern.FromTokens([SheetTextToken.Literal("Lottery Number: "), SheetTextToken.Gap]),
            SheetTextPattern.FromTokens([SheetTextToken.Literal("Plot "), SheetTextToken.Gap, SheetTextToken.Literal(", "), SheetTextToken.Gap, SheetTextToken.Literal(" Ward, "), SheetTextToken.Gap, SheetTextToken.Literal(" ("), SheetTextToken.Gap, SheetTextToken.Literal(")")]),
            SheetTextPattern.FromTokens([SheetTextToken.Literal("Type of Entry: "), SheetTextToken.Gap]));

    private static byte[] Record(byte plot, byte ward, ushort territory, uint number, byte buyer, byte phase)
    {
        var record = new byte[LotteryEntryLayout.Length];
        record[0x00] = plot;
        record[0x02] = ward;
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x04), territory);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(0x08), number);
        record[0x0C] = buyer;
        record[0x0D] = phase;
        return record;
    }
}
