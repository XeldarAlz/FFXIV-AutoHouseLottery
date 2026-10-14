using AutoHouseLottery.Core.Game.Model;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace AutoHouseLottery.Core.Game.Placard;

// Reads the open placard from every source there is: the sale handler's capture for this window, the agent's fields
// and the window's text. Each source is kept apart in the reading, so a disagreement shows instead of being merged away.
internal static unsafe class PlacardReader
{
    public const string AddonName = "HousingSignBoard";

    // HousingHerald and AltMate read the address from text node 56 and the price and entry end from node 64.
    private const uint AddressTextNodeId = 56;
    private const uint DetailsTextNodeId = 64;

    public static PlacardReading? Read(AddonUpdateCounter window, PlacardSaleHook sales, SignboardAgentLayout layout)
    {
        if (!AddonReads.TryGetReady(AddonName, out var addon))
        {
            return null;
        }

        var addressText = AddonReads.NodeText(addon->GetTextNodeById(AddressTextNodeId));
        var identitySource = addressText is null ? PlacardSources.None : PlacardSources.AddonText;
        var saleSource = PlacardSources.None;
        uint? territoryTypeId = null;
        int? wardIndex = null;
        int? plotIndex = null;
        short? apartmentNumber = null;
        PlacardHousingType? housingType = null;
        PlacardSaleInfo? sale = null;

        if (sales.Latest is { } capture && PlacardCaptureRule.BelongsToOpenWindow(capture.Sequence, window.LastClosedSequence, windowOpen: true))
        {
            territoryTypeId = capture.TerritoryTypeId;
            wardIndex = capture.WardIndex;
            plotIndex = capture.PlotIndex;
            apartmentNumber = capture.ApartmentNumber;
            housingType = capture.HousingType;
            identitySource |= PlacardSources.SaleInfoHook;
            if (capture.Sale is { } captured)
            {
                sale = captured;
                saleSource = PlacardSources.SaleInfoHook;
            }
        }

        bool? forSale = null;
        byte? size = null;
        ulong? ownerFreeCompanyId = null;
        var agent = AgentHousingSignboard.Instance();
        if (agent != null)
        {
            forSale = layout.ReadByte(agent, layout.ForSaleOffset) is { } forSaleByte ? forSaleByte != 0 : null;
            size = layout.ReadByte(agent, layout.SizeOffset);
            ownerFreeCompanyId = layout.ReadUInt64(agent, layout.OwnerFreeCompanyOffset);
            if (territoryTypeId is null && ReadAgentAddress(agent, layout) is { } agentAddress)
            {
                territoryTypeId = agentAddress.TerritoryTypeId;
                wardIndex = agentAddress.WardIndex;
                plotIndex = agentAddress.PlotIndex;
                identitySource |= PlacardSources.AgentFields;
            }

            // The agent keeps the last sale block it was given, so it describes this placard only when the agent says this
            // plot is for sale.
            if (sale is null && forSale == true && layout.ReadSaleBlock(agent) is { } agentSale)
            {
                sale = agentSale;
                saleSource = PlacardSources.AgentSaleBlock;
            }
        }

        return new PlacardReading(
            territoryTypeId,
            wardIndex,
            plotIndex,
            apartmentNumber,
            housingType,
            sale,
            forSale,
            size,
            ownerFreeCompanyId,
            addressText,
            AddonReads.NodeText(addon->GetTextNodeById(DetailsTextNodeId)),
            PlacardButtons.Collect(addon),
            identitySource,
            saleSource,
            window.LastUpdateSequence,
            DateTime.UtcNow);
    }

    // The agent's own copy of the address, independent of the hook, so the probe can compare the two.
    public static (uint TerritoryTypeId, int WardIndex, int PlotIndex)? ReadAgentAddress(AgentHousingSignboard* agent, SignboardAgentLayout layout)
    {
        if (layout.ReadUInt16(agent, layout.TerritoryOffset) is not { } territoryTypeId
            || layout.ReadByte(agent, layout.WardOffset) is not { } wardIndex
            || layout.ReadByte(agent, layout.PlotOffset) is not { } plotIndex)
        {
            return null;
        }

        return (territoryTypeId, wardIndex, plotIndex);
    }
}
