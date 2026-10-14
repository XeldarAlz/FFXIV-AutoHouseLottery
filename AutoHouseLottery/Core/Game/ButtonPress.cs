using AutoHouseLottery.Core.Game.Model;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AutoHouseLottery.Core.Game;

// Dispatches a button's own click event, the way a mouse click does, after checking everything the dispatch reads:
// the owner node, that it is visible and enabled, and that it has an event at all.
internal static unsafe class ButtonPress
{
    public static bool Visible(AtkComponentButton* button)
    {
        var owner = button->AtkComponentBase.OwnerNode;
        return owner != null && owner->AtkResNode.IsVisible();
    }

    public static DialogActionResult Press(AtkUnitBase* addon, AtkComponentButton* button)
    {
        var owner = button->AtkComponentBase.OwnerNode;
        if (owner == null)
        {
            return DialogActionResult.ButtonMissing;
        }

        if (!button->IsEnabled || !owner->AtkResNode.IsVisible())
        {
            return DialogActionResult.ButtonDisabled;
        }

        var clickEvent = owner->AtkResNode.AtkEventManager.Event;
        if (clickEvent == null)
        {
            return DialogActionResult.ButtonMissing;
        }

        addon->ReceiveEvent(clickEvent->State.EventType, unchecked((int)clickEvent->Param), clickEvent);
        return DialogActionResult.Pressed;
    }
}
