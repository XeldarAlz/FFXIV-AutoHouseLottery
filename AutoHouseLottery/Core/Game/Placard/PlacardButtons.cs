using AutoHouseLottery.Core.Game.Dialogs;
using AutoHouseLottery.Core.Game.Model;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Game.Placard;

// The placard's buttons, found by walking its nodes and identified by their label against the sheet rows, since the
// node IDs and callback values of the lottery buttons are not documented.
internal static unsafe class PlacardButtons
{
    public static readonly HousingText[] LotteryButtons = [HousingText.EnterLotteryButton, HousingText.ViewResultsButton];

    // Component nodes carry their component's ID as the node type, and those IDs start at 1000.
    private const ushort FirstComponentNodeType = 1000;

    public static ImmutableArray<PlacardButton> Collect(AtkUnitBase* addon)
    {
        var buttons = ImmutableArray.CreateBuilder<PlacardButton>();
        var nodes = addon->UldManager.NodeList;
        for (var nodeIndex = 0; nodeIndex < addon->UldManager.NodeListCount; nodeIndex++)
        {
            var node = nodes[nodeIndex];
            var button = AsButton(node);
            if (button == null)
            {
                continue;
            }

            var text = AddonReads.NodeText(button->ButtonTextNode) ?? string.Empty;
            buttons.Add(new PlacardButton(node->NodeId, text, button->IsEnabled, AddonReads.EventParam(button), HousingSheetText.Match(text, LotteryButtons)));
        }

        return buttons.ToImmutable();
    }

    public static AtkComponentButton* Find(AtkUnitBase* addon, HousingText label)
    {
        var nodes = addon->UldManager.NodeList;
        for (var nodeIndex = 0; nodeIndex < addon->UldManager.NodeListCount; nodeIndex++)
        {
            var button = AsButton(nodes[nodeIndex]);
            if (button != null && HousingSheetText.Matches(label, AddonReads.NodeText(button->ButtonTextNode) ?? string.Empty))
            {
                return button;
            }
        }

        return null;
    }

    private static AtkComponentButton* AsButton(AtkResNode* node)
        => node == null || (ushort)node->Type < FirstComponentNodeType ? null : node->GetAsAtkComponentButton();
}
