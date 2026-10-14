using AutoHouseLottery.Core.Game.Dialogs;
using AutoHouseLottery.Core.Game.Model;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace AutoHouseLottery.Core.Game.Placard;

// Reads the open placard from every source there is and keeps each one's address apart: the sale handler's capture
// for this window, the agent's fields and the window's text. The reading states whether they agree; a disagreement
// shows instead of being merged away.
internal static unsafe class PlacardReader
{
    public const string AddonName = "HousingSignBoard";

    // HousingHerald and AltMate read the address from text node 56 and the price and entry end from node 64.
    private const uint AddressTextNodeId = 56;
    private const uint DetailsTextNodeId = 64;

    private static readonly HousingText[] lotteryButtons = [HousingText.EnterLotteryButton, HousingText.ViewResultsButton];
    private static readonly Func<uint, string?> districtNameOf = DistrictNames.Of;

    public static PlacardReading? Read(AddonUpdateCounter window, PlacardSaleHook sales, SignboardAgentLayout layout)
    {
        if (!AddonReads.TryGetReady(AddonName, out var addon))
        {
            return null;
        }

        PlacardSaleCapture? capture = sales.Latest is { } latest && PlacardCaptureRule.BelongsToOpenWindow(latest.Sequence, window.LastClosedSequence, windowOpen: true)
            ? latest
            : null;

        bool? forSale = null;
        byte? size = null;
        ulong? ownerFreeCompanyId = null;
        PlotAddress? agentAddress = null;
        PlacardSaleInfo? agentSale = null;
        var agent = AgentHousingSignboard.Instance();
        if (agent != null)
        {
            forSale = layout.ReadByte(agent, layout.ForSaleOffset) is { } forSaleByte ? forSaleByte != 0 : null;
            size = layout.ReadByte(agent, layout.SizeOffset);
            ownerFreeCompanyId = layout.ReadUInt64(agent, layout.OwnerFreeCompanyOffset);
            agentAddress = ReadAgentAddress(agent, layout);
            agentSale = layout.ReadSaleBlock(agent);
        }

        var saleSource = PlacardSaleRule.Choose(capture, forSale, agentSale, out var sale);
        var hookAddress = capture?.Address;
        var addressText = AddonReads.NodeText(addon->GetTextNodeById(AddressTextNodeId));
        return new PlacardReading(
            hookAddress,
            agentAddress,
            addressText,
            PlacardAddressRule.Evaluate(hookAddress, agentAddress, addressText, districtNameOf),
            capture?.ApartmentNumber,
            capture?.HousingType,
            sale,
            saleSource,
            forSale,
            size,
            ownerFreeCompanyId,
            AddonReads.NodeText(addon->GetTextNodeById(DetailsTextNodeId)),
            LabelledButtons.Collect(addon, lotteryButtons),
            window.LastUpdateSequence,
            DateTime.UtcNow);
    }

    private static PlotAddress? ReadAgentAddress(AgentHousingSignboard* agent, SignboardAgentLayout layout)
    {
        if (layout.ReadUInt16(agent, layout.TerritoryOffset) is not { } territoryTypeId
            || layout.ReadByte(agent, layout.WardOffset) is not { } wardIndex
            || layout.ReadByte(agent, layout.PlotOffset) is not { } plotIndex)
        {
            return null;
        }

        return new PlotAddress(territoryTypeId, wardIndex, plotIndex);
    }
}
