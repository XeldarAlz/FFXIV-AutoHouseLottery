using System.Buffers.Binary;

namespace AutoHouseLottery.Core.Game.Model;

internal enum PlacardPurchaseType : byte
{
    Unavailable = 0,
    FirstComeFirstServed = 1,
    Lottery = 2,
}

internal enum PlacardTenantType : byte
{
    FreeCompany = 1,
    Private = 2,
}

internal enum PlacardAvailability : byte
{
    Available = 1,
    InResultsPeriod = 2,
    Unavailable = 3,
}

internal enum PlacardHousingType : byte
{
    OwnedHouse = 0,
    UnownedHouse = 1,
    FreeCompanyApartment = 2,
    Apartment = 3,
}

// The placard's sale information block as PaissaHouse documents it. A value outside the known set reads as null with
// its raw byte kept, so an unknown availability never becomes Available.
internal readonly record struct PlacardSaleInfo(
    PlacardPurchaseType? PurchaseType,
    PlacardTenantType? TenantType,
    PlacardAvailability? Availability,
    DateTime? PhaseEndsAtUtc,
    uint EntryCount,
    byte RawPurchaseType,
    byte RawTenantType,
    byte RawAvailability)
{
    public const int Length = EntryCountOffset + sizeof(uint);

    private const int PurchaseTypeOffset = 0x00;
    private const int TenantTypeOffset = 0x01;
    private const int AvailabilityOffset = 0x02;
    private const int PhaseEndOffset = 0x08;
    private const int EntryCountOffset = 0x10;
    private const uint NoPhaseEnd = 0;

    public static PlacardSaleInfo Parse(ReadOnlySpan<byte> block)
    {
        var rawPurchaseType = block[PurchaseTypeOffset];
        var rawTenantType = block[TenantTypeOffset];
        var rawAvailability = block[AvailabilityOffset];
        var phaseEnd = BinaryPrimitives.ReadUInt32LittleEndian(block[PhaseEndOffset..]);
        return new PlacardSaleInfo(
            rawPurchaseType <= (byte)PlacardPurchaseType.Lottery ? (PlacardPurchaseType)rawPurchaseType : null,
            rawTenantType is (byte)PlacardTenantType.FreeCompany or (byte)PlacardTenantType.Private ? (PlacardTenantType)rawTenantType : null,
            rawAvailability is >= (byte)PlacardAvailability.Available and <= (byte)PlacardAvailability.Unavailable ? (PlacardAvailability)rawAvailability : null,
            phaseEnd == NoPhaseEnd ? null : DateTime.UnixEpoch.AddSeconds(phaseEnd),
            BinaryPrimitives.ReadUInt32LittleEndian(block[EntryCountOffset..]),
            rawPurchaseType,
            rawTenantType,
            rawAvailability);
    }
}
