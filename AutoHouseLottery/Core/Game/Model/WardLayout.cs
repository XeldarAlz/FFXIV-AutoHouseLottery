namespace AutoHouseLottery.Core.Game.Model;

// AtkValue positions of the HousingSelectBlock ward list, from the recovered HouseHunter reader and LlamaLibrary's
// HousingSelectBlock window. A ward whose values do not fit this layout is not read.
internal static class WardLayout
{
    public const int WardIndexValue = 1;
    public const int WardNameValue = 2;
    public const int BuyerFlagsValue = 3;
    public const int WardCountValue = 4;
    public const int RowCountValue = 35;
    public const int FirstRowValue = 36;
    public const int ValuesPerRow = 7;
    public const int MaxRows = ResidentialDistricts.PlotsPerWard;

    public const int StatusOffset = 0;
    public const int SizeTextOffset = 1;
    public const int NameOrPriceOffset = 2;
    public const int UnidentifiedOffset = 3;
    public const int FirstTagOffset = 4;
    public const int TagCount = 3;

    // The ward's buyer flags sit in the top byte of value 3: bit 0 there marks a Free Company ward, bit 1 a private one.
    private const int BuyerFlagsByteShift = 24;
    private const uint FreeCompanyBit = 1;
    private const uint PrivateBit = 2;

    public static int RowValue(int rowIndex, int offset) => FirstRowValue + (rowIndex * ValuesPerRow) + offset;

    public static bool RowCountFits(int rowCount, int valueCount)
        => rowCount is >= 0 and <= MaxRows && RowValue(rowCount, StatusOffset) <= valueCount;

    public static bool FreeCompanyFlag(uint buyerFlags) => ((buyerFlags >> BuyerFlagsByteShift) & FreeCompanyBit) != 0;

    public static bool PrivateFlag(uint buyerFlags) => ((buyerFlags >> BuyerFlagsByteShift) & PrivateBit) != 0;
}
