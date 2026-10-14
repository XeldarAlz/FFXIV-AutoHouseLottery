using AutoHouseLottery.Core.Game.Model;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Game.Dialogs;

// The buttons of a housing window, found by walking its nodes and identified by their own label against the Addon
// rows, since node IDs and callback values can move between builds.
internal static unsafe class LabelledButtons
{
    // Component nodes carry their component's ID as the node type, and those IDs start at 1000.
    private const ushort FirstComponentNodeType = 1000;

    public static ImmutableArray<WindowButton> Collect(AtkUnitBase* addon, ReadOnlySpan<HousingText> labels)
    {
        var buttons = ImmutableArray.CreateBuilder<WindowButton>();
        var nodes = addon->UldManager.NodeList;
        for (var nodeIndex = 0; nodeIndex < addon->UldManager.NodeListCount; nodeIndex++)
        {
            var node = nodes[nodeIndex];
            var button = AsButton(node);
            if (button == null)
            {
                continue;
            }

            var label = Label(button);
            buttons.Add(new WindowButton(node->NodeId, label, button->IsEnabled, ButtonPress.Visible(button), AddonReads.EventParam(button), HousingSheetText.Match(label, labels)));
        }

        return buttons.ToImmutable();
    }

    public static AtkComponentButton* Find(AtkUnitBase* addon, ReadOnlySpan<HousingText> labels)
    {
        var nodes = addon->UldManager.NodeList;
        for (var nodeIndex = 0; nodeIndex < addon->UldManager.NodeListCount; nodeIndex++)
        {
            var button = AsButton(nodes[nodeIndex]);
            if (button != null && ButtonPress.Visible(button) && HousingSheetText.Match(Label(button), labels) is not null)
            {
                return button;
            }
        }

        return null;
    }

    public static ImmutableArray<WindowCheckBox> CollectCheckBoxes(AtkUnitBase* addon, ReadOnlySpan<HousingText> labels)
    {
        var checkBoxes = ImmutableArray.CreateBuilder<WindowCheckBox>();
        var nodes = addon->UldManager.NodeList;
        for (var nodeIndex = 0; nodeIndex < addon->UldManager.NodeListCount; nodeIndex++)
        {
            var node = nodes[nodeIndex];
            var checkBox = AsCheckBox(node);
            if (checkBox == null)
            {
                continue;
            }

            var button = &checkBox->AtkComponentButton;
            var label = Label(button);
            checkBoxes.Add(new WindowCheckBox(node->NodeId, label, button->IsEnabled, ButtonPress.Visible(button), button->IsChecked, HousingSheetText.Match(label, labels)));
        }

        return checkBoxes.ToImmutable();
    }

    // A checkbox is a button underneath; its label is the button's own text.
    public static AtkComponentButton* FindCheckBox(AtkUnitBase* addon, ReadOnlySpan<HousingText> labels)
    {
        var nodes = addon->UldManager.NodeList;
        for (var nodeIndex = 0; nodeIndex < addon->UldManager.NodeListCount; nodeIndex++)
        {
            var checkBox = AsCheckBox(nodes[nodeIndex]);
            if (checkBox == null)
            {
                continue;
            }

            var button = &checkBox->AtkComponentButton;
            if (ButtonPress.Visible(button) && HousingSheetText.Match(Label(button), labels) is not null)
            {
                return button;
            }
        }

        return null;
    }

    public static string Label(AtkComponentButton* button) => AddonReads.NodeText(button->ButtonTextNode) ?? string.Empty;

    // The press rule applied to this button's label as the game shows it now.
    public static bool MayPress(AtkComponentButton* button, ReadOnlySpan<HousingText> expected, bool requestCommits)
    {
        var label = Label(button);
        return ButtonLabelRule.MayPress(expected, requestCommits, text => HousingSheetText.Matches(text, label));
    }

    private static AtkComponentButton* AsButton(AtkResNode* node)
        => node == null || (ushort)node->Type < FirstComponentNodeType ? null : node->GetAsAtkComponentButton();

    private static AtkComponentCheckBox* AsCheckBox(AtkResNode* node)
        => node == null || (ushort)node->Type < FirstComponentNodeType ? null : node->GetAsAtkComponentCheckBox();
}
