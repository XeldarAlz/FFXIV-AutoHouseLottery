using ECommons;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Text.ReadOnly;
using System.Text;

namespace AutoHouseLottery.Core.Game;

internal static unsafe class AddonReads
{
    public const int NoEventParam = -1;

    public static bool TryGetReady(string addonName, out AtkUnitBase* addon)
        => GenericHelpers.TryGetAddonByName(addonName, out addon) && GenericHelpers.IsAddonReady(addon);

    public static bool IsInteger(in AtkValue value) => value.Type is AtkValueType.Int or AtkValueType.UInt;

    public static uint Unsigned(in AtkValue value) => value.Type switch
    {
        AtkValueType.Int => unchecked((uint)value.Int),
        AtkValueType.UInt => value.UInt,
        AtkValueType.Bool => value.Byte,
        _ => 0,
    };

    public static bool IsText(in AtkValue value)
        => (value.Type is AtkValueType.String or AtkValueType.ManagedString or AtkValueType.ConstString) && value.String.HasValue;

    public static string Text(in AtkValue value)
        => IsText(value) ? new ReadOnlySeStringSpan(value.String.Value).ExtractText() : string.Empty;

    public static bool? Bool(AtkUnitBase* addon, int valueIndex)
    {
        if (addon->AtkValues == null || valueIndex >= addon->AtkValuesCount)
        {
            return null;
        }

        ref readonly var value = ref addon->AtkValues[valueIndex];
        return value.Type == AtkValueType.Bool ? value.Byte != 0 : null;
    }

    public static string? NodeText(AtkTextNode* node)
        => node == null ? null : new ReadOnlySeStringSpan(node->NodeText.AsSpan()).ExtractText();

    public static int EventParam(AtkComponentButton* button)
    {
        var owner = button->AtkComponentBase.OwnerNode;
        if (owner == null)
        {
            return NoEventParam;
        }

        var firstEvent = owner->AtkResNode.AtkEventManager.Event;
        return firstEvent == null ? NoEventParam : unchecked((int)firstEvent->Param);
    }

    // Every string value of the window, for dialogs whose text position is not documented.
    public static int CollectTexts(AtkUnitBase* addon, Span<string?> destination)
    {
        if (addon->AtkValues == null)
        {
            return 0;
        }

        var written = 0;
        for (var valueIndex = 0; valueIndex < addon->AtkValuesCount && written < destination.Length; valueIndex++)
        {
            ref readonly var value = ref addon->AtkValues[valueIndex];
            if (!IsText(value))
            {
                continue;
            }

            var text = Text(value);
            if (text.Length > 0)
            {
                destination[written++] = text;
            }
        }

        return written;
    }

    public static string Describe(AtkUnitBase* addon, int maxValues)
    {
        if (addon->AtkValues == null)
        {
            return "no values";
        }

        var valueCount = Math.Min((int)addon->AtkValuesCount, maxValues);
        var builder = new StringBuilder();
        builder.Append($"{addon->AtkValuesCount} values");
        for (var valueIndex = 0; valueIndex < valueCount; valueIndex++)
        {
            ref readonly var value = ref addon->AtkValues[valueIndex];
            builder.Append($"; [{valueIndex}] {value.Type}");
            if (IsText(value))
            {
                builder.Append($" '{Text(value)}'");
            }
            else if (value.Type is AtkValueType.Int or AtkValueType.UInt or AtkValueType.Bool)
            {
                builder.Append($" {Unsigned(value)}");
            }
        }

        return builder.ToString();
    }
}
