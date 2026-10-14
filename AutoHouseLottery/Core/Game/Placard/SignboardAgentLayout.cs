using AutoHouseLottery.Core.Game.Model;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace AutoHouseLottery.Core.Game.Placard;

// Where AgentHousingSignboard keeps the placard's fields. ClientStructs types none of them, so each offset is read at
// load from the game code that writes the field, using LlamaLibrary's AgentHousingSignBoardOffsets patterns (MIT,
// maintained for 7.3). An offset whose pattern is missing, or that would fall outside the agent, stays unknown.
internal sealed unsafe class SignboardAgentLayout
{
    private const int NotResolved = -1;

    private static readonly OffsetPattern wardPattern = new("WardIndex", "44 88 63 ?? 40 88 7B ??", 3, false, sizeof(byte));
    private static readonly OffsetPattern plotPattern = new("PlotIndex", "40 88 7B ?? 66 44 89 43 ??", 3, false, sizeof(byte));
    private static readonly OffsetPattern territoryPattern = new("TerritoryTypeId", "66 44 89 7B ?? 44 88 63 ??", 4, false, sizeof(ushort));
    private static readonly OffsetPattern forSalePattern = new("IsForSale", "40 88 7B ?? 88 43 ?? E8 ?? ?? ?? ??", 3, false, sizeof(byte));
    private static readonly OffsetPattern sizePattern = new("PlotSize", "88 43 ?? E8 ?? ?? ?? ?? 48 8B 4B ?? 89 7D ??", 2, false, sizeof(byte));
    private static readonly OffsetPattern ownerFreeCompanyPattern = new("OwnerFreeCompanyId", "48 89 86 ?? ?? ?? ?? 48 8B 01 FF 50 ?? 4D 8D 86 ?? ?? ?? ??", 3, true, sizeof(ulong));

    // PaissaHouse's placard sale signature: its two movups store the 32-byte sale information block at agent + 0x48,
    // and the signature fixes that displacement, so a match proves the copy lands there on this build.
    private const int SaleBlockOffset = 0x48;

    private SignboardAgentLayout(int ward, int plot, int territory, int forSale, int size, int ownerFreeCompany, bool saleBlock)
    {
        WardOffset = ward;
        PlotOffset = plot;
        TerritoryOffset = territory;
        ForSaleOffset = forSale;
        SizeOffset = size;
        OwnerFreeCompanyOffset = ownerFreeCompany;
        SaleBlockResolved = saleBlock;
    }

    public int WardOffset { get; }

    public int PlotOffset { get; }

    public int TerritoryOffset { get; }

    public int ForSaleOffset { get; }

    public int SizeOffset { get; }

    public int OwnerFreeCompanyOffset { get; }

    public bool SaleBlockResolved { get; }

    public static SignboardAgentLayout Resolve(bool saleSignatureFound)
    {
        var layout = new SignboardAgentLayout(
            ResolveOffset(wardPattern),
            ResolveOffset(plotPattern),
            ResolveOffset(territoryPattern),
            ResolveOffset(forSalePattern),
            ResolveOffset(sizePattern),
            ResolveOffset(ownerFreeCompanyPattern),
            saleSignatureFound);
        RunLog.Info($"Placard agent layout: {layout.Describe()}.");
        return layout;
    }

    public string Describe()
        => $"ward {Format(WardOffset)}, plot {Format(PlotOffset)}, territory {Format(TerritoryOffset)}, for sale {Format(ForSaleOffset)}, "
         + $"size {Format(SizeOffset)}, owner FC {Format(OwnerFreeCompanyOffset)}, sale block {(SaleBlockResolved ? $"0x{SaleBlockOffset:X}" : "unresolved")}";

    public byte? ReadByte(AgentHousingSignboard* agent, int offset) => offset == NotResolved ? null : ((byte*)agent)[offset];

    public ushort? ReadUInt16(AgentHousingSignboard* agent, int offset) => offset == NotResolved ? null : *(ushort*)((byte*)agent + offset);

    public ulong? ReadUInt64(AgentHousingSignboard* agent, int offset) => offset == NotResolved ? null : *(ulong*)((byte*)agent + offset);

    public PlacardSaleInfo? ReadSaleBlock(AgentHousingSignboard* agent)
        => SaleBlockResolved ? PlacardSaleInfo.Parse(new ReadOnlySpan<byte>((byte*)agent + SaleBlockOffset, PlacardSaleInfo.Length)) : null;

    private static int ResolveOffset(in OffsetPattern pattern)
    {
        if (!Svc.SigScanner.TryScanText(pattern.Signature, out var match))
        {
            RunLog.Warning($"Placard agent layout: the {pattern.Name} pattern was not found on this build.");
            return NotResolved;
        }

        var operand = (byte*)match + pattern.OperandPosition;
        var offset = pattern.WideOperand ? *(int*)operand : *operand;
        if (offset < 0 || offset + pattern.FieldSize > AgentHousingSignboard.StructSize)
        {
            RunLog.Warning($"Placard agent layout: {pattern.Name} resolved to 0x{offset:X}, outside the agent; it stays unknown.");
            return NotResolved;
        }

        return offset;
    }

    private static string Format(int offset) => offset == NotResolved ? "unresolved" : $"0x{offset:X}";

    private readonly record struct OffsetPattern(string Name, string Signature, int OperandPosition, bool WideOperand, int FieldSize);
}
