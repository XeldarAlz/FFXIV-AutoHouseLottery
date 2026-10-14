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
        Assert.Equal(new LotteryEntry(Mist, 11, 4, 1234, LotteryBuyerType.Private, LotteryPhase.EntryPeriod, 2, 1), entry);
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
        Assert.Equal(9, entry.RawPhase);
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
        Assert.True(new LotterySnapshot(LotterySnapshotState.LoadedNone, 1, null, now).Loaded);
        Assert.True(new LotterySnapshot(LotterySnapshotState.Active, 1, null, now).Loaded);
        Assert.False(new LotterySnapshot(LotterySnapshotState.Unloaded, 1, null, now).Loaded);
        Assert.False(new LotterySnapshot(LotterySnapshotState.Invalid, 1, null, now).Loaded);
        Assert.False(new LotterySnapshot(LotterySnapshotState.NoCharacter, 0, null, now).Loaded);
    }

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
