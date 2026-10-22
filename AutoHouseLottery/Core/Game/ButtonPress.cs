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

    public static DialogActionResult Press(AtkUnitBase* addon, AtkComponentButton* button) => Click(addon, button, tick: false);

    // A mouse click on a checkbox flips the box's checked flag inside the component before the window hears the click,
    // and the window's click handler only reads that flag to decide whether its buttons unlock. The click event alone
    // leaves the box unticked, so a tick sets the flag through the game's own SetChecked first.
    public static DialogActionResult Tick(AtkUnitBase* addon, AtkComponentButton* checkBox) => Click(addon, checkBox, tick: true);

    private static DialogActionResult Click(AtkUnitBase* addon, AtkComponentButton* button, bool tick)
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

        if (tick)
        {
            button->SetChecked(true);
        }

        addon->ReceiveEvent(clickEvent->State.EventType, unchecked((int)clickEvent->Param), clickEvent);
        return DialogActionResult.Pressed;
    }
}
