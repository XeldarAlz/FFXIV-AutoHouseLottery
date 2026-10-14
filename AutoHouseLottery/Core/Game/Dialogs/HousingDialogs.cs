using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Game.Placard;
using ECommons.Automation.UIInput;
using ECommons.Throttlers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AutoHouseLottery.Core.Game.Dialogs;

// Recognises the lottery dialogs by their sheet rows and presses their buttons. Every press is refused unless its
// action is permitted, the window is open and ready, and the window's text is the expected row; there is no generic
// "answer whatever is open".
internal static unsafe class HousingDialogs
{
    public const string EntryConfirmationAddon = "SelectYesNoTextScroll";
    public const string ResultAddon = "SelectYesno";

    // LlamaLibrary's HousingSignBoard.ClickBuy fires callback 1 for the placard's purchase button. Unverified for the
    // lottery buttons, so presses dispatch the identified button's own click event and the probe logs each button's
    // event parameter next to this candidate.
    public const int PlacardButtonCallbackCandidate = 1;

    // HousingHerald: on the entry confirmation, Confirm is component node 5 and Cancel node 6.
    private const uint ConfirmButtonNodeId = 5;
    private const uint CancelButtonNodeId = 6;
    private const int MaxDialogTexts = 16;
    private const int ClickThrottleMs = 600;
    private const string ClickThrottleKey = "AutoHouseLottery.HousingDialogs.Click";

    private static readonly HousingText[] entryConfirmation = [HousingText.EntryConfirmation];

    public static DialogIdentity? IdentifyEntryConfirmation()
    {
        if (!AddonReads.TryGetReady(EntryConfirmationAddon, out var addon))
        {
            return null;
        }

        Span<string?> texts = new string?[MaxDialogTexts];
        var textCount = AddonReads.CollectTexts(addon, texts);
        for (var textIndex = 0; textIndex < textCount; textIndex++)
        {
            if (HousingSheetText.Match(texts[textIndex]!, entryConfirmation) is { } matched)
            {
                return new DialogIdentity(EntryConfirmationAddon, matched, texts[textIndex]!);
            }
        }

        return new DialogIdentity(EntryConfirmationAddon, null, textCount > 0 ? texts[0]! : string.Empty);
    }

    public static DialogIdentity? IdentifyResultDialog()
    {
        if (!AddonReads.TryGetReady(ResultAddon, out var addon))
        {
            return null;
        }

        var shown = new AddonMaster.SelectYesno(addon).Text;
        return new DialogIdentity(ResultAddon, HousingSheetText.Match(shown, HousingTextRows.ResultDialogs.AsSpan()), shown);
    }

    public static DialogActionResult PressPlacardButton(HousingText label)
    {
        var action = label switch
        {
            HousingText.EnterLotteryButton => NativeAction.PressEnterLottery,
            HousingText.ViewResultsButton => NativeAction.PressViewResults,
            _ => NativeAction.None,
        };
        if (!NativeActionGate.IsPermitted(action))
        {
            return Refused(action, DialogActionResult.NotPermitted);
        }

        if (!AddonReads.TryGetReady(PlacardReader.AddonName, out var addon))
        {
            return Refused(action, DialogActionResult.WindowNotOpen);
        }

        var button = PlacardButtons.Find(addon, label);
        if (button == null)
        {
            return Refused(action, DialogActionResult.ButtonMissing);
        }

        return Press(action, addon, button);
    }

    public static DialogActionResult ConfirmEntry() => PressEntryConfirmation(NativeAction.ConfirmEntry, ConfirmButtonNodeId);

    public static DialogActionResult CancelEntry() => PressEntryConfirmation(NativeAction.CancelEntry, CancelButtonNodeId);

    // Yes on a result dialog claims the plot or collects a refund, so the caller names which result it expects.
    public static DialogActionResult AcceptResult(HousingText expected) => AnswerResult(NativeAction.AcceptResult, expected);

    public static DialogActionResult DeclineResult(HousingText expected) => AnswerResult(NativeAction.DeclineResult, expected);

    private static DialogActionResult PressEntryConfirmation(NativeAction action, uint buttonNodeId)
    {
        if (!NativeActionGate.IsPermitted(action))
        {
            return Refused(action, DialogActionResult.NotPermitted);
        }

        if (IdentifyEntryConfirmation() is not { } identity)
        {
            return Refused(action, DialogActionResult.WindowNotOpen);
        }

        if (identity.Text != HousingText.EntryConfirmation || !AddonReads.TryGetReady(EntryConfirmationAddon, out var addon))
        {
            return Refused(action, DialogActionResult.IdentityMismatch);
        }

        var button = addon->GetComponentButtonById(buttonNodeId);
        return button == null ? Refused(action, DialogActionResult.ButtonMissing) : Press(action, addon, button);
    }

    private static DialogActionResult AnswerResult(NativeAction action, HousingText expected)
    {
        if (!NativeActionGate.IsPermitted(action))
        {
            return Refused(action, DialogActionResult.NotPermitted);
        }

        if (IdentifyResultDialog() is not { } identity)
        {
            return Refused(action, DialogActionResult.WindowNotOpen);
        }

        if (identity.Text != expected || !AddonReads.TryGetReady(ResultAddon, out var addon))
        {
            return Refused(action, DialogActionResult.IdentityMismatch);
        }

        var dialog = (AddonSelectYesno*)addon;
        var button = action == NativeAction.AcceptResult ? dialog->YesButton : dialog->NoButton;
        return button == null ? Refused(action, DialogActionResult.ButtonMissing) : Press(action, addon, button);
    }

    private static DialogActionResult Press(NativeAction action, AtkUnitBase* addon, AtkComponentButton* button)
    {
        if (!button->IsEnabled)
        {
            return Refused(action, DialogActionResult.ButtonDisabled);
        }

        if (!EzThrottler.Throttle(ClickThrottleKey, ClickThrottleMs))
        {
            return DialogActionResult.Throttled;
        }

        RunLog.Info($"Housing dialog: {action} pressed.");
        button->ClickAddonButton(addon);
        return DialogActionResult.Pressed;
    }

    private static DialogActionResult Refused(NativeAction action, DialogActionResult result)
    {
        RunLog.Info($"Housing dialog: {action} refused ({result}).");
        return result;
    }
}
