using AutoHouseLottery.Core.Game.Model;
using System.Buffers.Binary;

namespace AutoHouseLottery.Tests.Game;

public sealed class PlacardSaleInfoTests
{
    private const uint PhaseEnd = 1_760_000_000;

    [Fact]
    public void ReadsALotteryPlotOpenForPrivateEntries()
    {
        var sale = PlacardSaleInfo.Parse(Block(purchaseType: 2, tenantType: 2, availability: 1, PhaseEnd, entryCount: 14));
        Assert.Equal(PlacardPurchaseType.Lottery, sale.PurchaseType);
        Assert.Equal(PlacardTenantType.Private, sale.TenantType);
        Assert.Equal(PlacardAvailability.Available, sale.Availability);
        Assert.Equal(DateTime.UnixEpoch.AddSeconds(PhaseEnd), sale.PhaseEndsAtUtc);
        Assert.Equal(14u, sale.EntryCount);
    }

    [Fact]
    public void ReadsAFreeCompanyPlotInItsResultsPeriod()
    {
        var sale = PlacardSaleInfo.Parse(Block(purchaseType: 2, tenantType: 1, availability: 2, PhaseEnd, entryCount: 3));
        Assert.Equal(PlacardTenantType.FreeCompany, sale.TenantType);
        Assert.Equal(PlacardAvailability.InResultsPeriod, sale.Availability);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(255)]
    public void AnUnknownAvailabilityNeverReadsAsAvailable(byte availability)
    {
        var sale = PlacardSaleInfo.Parse(Block(purchaseType: 2, tenantType: 2, availability, PhaseEnd, entryCount: 0));
        Assert.Null(sale.Availability);
        Assert.Equal(availability, sale.RawAvailability);
    }

    [Fact]
    public void UnknownPurchaseAndTenantValuesStayUnknown()
    {
        var sale = PlacardSaleInfo.Parse(Block(purchaseType: 9, tenantType: 0, availability: 1, PhaseEnd, entryCount: 0));
        Assert.Null(sale.PurchaseType);
        Assert.Null(sale.TenantType);
        Assert.Equal(9, sale.RawPurchaseType);
    }

    [Fact]
    public void AZeroPhaseEndIsNoDate()
    {
        Assert.Null(PlacardSaleInfo.Parse(Block(purchaseType: 2, tenantType: 2, availability: 3, phaseEnd: 0, entryCount: 0)).PhaseEndsAtUtc);
    }

    [Theory]
    [InlineData(5, 4, true, true)]
    [InlineData(3, 4, true, false)]
    [InlineData(5, 4, false, false)]
    public void ASaleCaptureBelongsToTheOpenPlacardOnlyIfTakenAfterTheLastClose(long captureSequence, long lastClosedSequence, bool windowOpen, bool expected)
    {
        Assert.Equal(expected, PlacardCaptureRule.BelongsToOpenWindow(captureSequence, lastClosedSequence, windowOpen));
    }

    private static byte[] Block(byte purchaseType, byte tenantType, byte availability, uint phaseEnd, uint entryCount)
    {
        var block = new byte[PlacardSaleInfo.Length];
        block[0x00] = purchaseType;
        block[0x01] = tenantType;
        block[0x02] = availability;
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(0x08), phaseEnd);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(0x10), entryCount);
        return block;
    }
}
