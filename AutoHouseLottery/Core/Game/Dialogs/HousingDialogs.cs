using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Game.Placard;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AutoHouseLottery.Core.Game.Dialogs;

// Recognises the lottery dialogs by their sheet rows and presses their buttons. Every press is refused unless its
// action is permitted on this thread, the window is open and ready, the window's text is the expected row, and the
// button's own label reads as the expected Addon row; there is no generic "answer whatever is open".
internal static unsafe class HousingDialogs
{
    public const string EntryConfirmationAddon = "SelectYesNoTextScroll";
    public const string ResultAddon = "SelectYesno";

    // LlamaLibrary's HousingSignBoard.ClickBuy fires callback 1 for the placard's purchase button. Unverified for the
    // lottery buttons, so presses dispatch the identified button's own click event and the probe logs each button's
    // event parameter next to this candidate.
    public const int PlacardButtonCallbackCandidate = 1;

    // Closing the entry confirmation fires its close callback; it has no path to the deposit.
    private const bool FireCloseCallback = true;
    private const int ClickThrottleMs = 600;
    private const string ClickThrottleKey = "AutoHouseLottery.HousingDialogs.Click";

    private static readonly HousingText[] entryConfirmation = [HousingText.EntryConfirmation];
    private static readonly HousingText[] confirmLabel = [HousingText.ConfirmLabel];

    public static DialogIdentity? IdentifyEntryConfirmation()
    {
        if (!AddonReads.TryGetReady(EntryConfirmationAddon, out var addon))
        {
            return null;
        }

        var firstText = string.Empty;
        if (addon->AtkValues != null)
        {
            for (var valueIndex = 0; valueIndex < addon->AtkValuesCount; valueIndex++)
            {
                ref readonly var value = ref addon->AtkValues[valueIndex];
                if (!AddonReads.IsText(value))
                {
                    continue;
                }

                var text = AddonReads.Text(value);
                if (HousingSheetText.Matches(HousingText.EntryConfirmation, text))
                {
                    return new DialogIdentity(EntryConfirmationAddon, HousingText.EntryConfirmation, text);
                }

                if (firstText.Length == 0)
                {
                    firstText = text;
                }
            }
        }

        return new DialogIdentity(EntryConfirmationAddon, null, firstText);
    }

    public static DialogIdentity? IdentifyResultDialog()
    {
        if (!AddonReads.TryGetReady(ResultAddon, out var addon))
        {
            return null;
        }

        var shown = AddonReads.NodeText(((AddonSelectYesno*)addon)->PromptText) ?? string.Empty;
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

        ReadOnlySpan<HousingText> expected = [label];
        var button = LabelledButtons.Find(addon, expected);
        if (button == null)
        {
            return Refused(action, DialogActionResult.ButtonMissing);
        }

        return Press(action, addon, button, expected, requestCommits: false);
    }

    public static DialogActionResult ConfirmEntry()
    {
        const NativeAction action = NativeAction.ConfirmEntry;
        if (!NativeActionGate.IsPermitted(action))
        {
            return Refused(action, DialogActionResult.NotPermitted);
        }

        if (!OpenEntryConfirmation(out var addon))
        {
            return Refused(action, DialogActionResult.IdentityMismatch);
        }

        var button = LabelledButtons.Find(addon, confirmLabel);
        return button == null ? Refused(action, DialogActionResult.ButtonMissing) : Press(action, addon, button, confirmLabel, requestCommits: true);
    }

    // Cancel never presses a button: the window is closed, so nothing on it can be confirmed by mistake.
    public static DialogActionResult CancelEntry()
    {
        const NativeAction action = NativeAction.CancelEntry;
        if (!NativeActionGate.IsPermitted(action))
        {
            return Refused(action, DialogActionResult.NotPermitted);
        }

        if (!OpenEntryConfirmation(out var addon))
        {
            return Refused(action, DialogActionResult.IdentityMismatch);
        }

        if (!EzThrottler.Throttle(ClickThrottleKey, ClickThrottleMs))
        {
            return DialogActionResult.Throttled;
        }

        RunLog.Info($"Housing dialog: {action} closes {EntryConfirmationAddon}.");
        addon->Close(FireCloseCallback);
        return DialogActionResult.Closed;
    }

    // Yes on a result dialog claims the plot or collects a refund, so the caller names which result it expects.
    public static DialogActionResult AcceptResult(HousingText expected)
        => AnswerResult(NativeAction.AcceptResult, expected, HousingTextRows.YesLabels.AsSpan(), requestCommits: true);

    public static DialogActionResult DeclineResult(HousingText expected)
        => AnswerResult(NativeAction.DeclineResult, expected, HousingTextRows.NoLabels.AsSpan(), requestCommits: false);

    private static bool OpenEntryConfirmation(out AtkUnitBase* addon)
    {
        addon = null;
        return IdentifyEntryConfirmation() is { Text: HousingText.EntryConfirmation } && AddonReads.TryGetReady(EntryConfirmationAddon, out addon);
    }

    private static DialogActionResult AnswerResult(NativeAction action, HousingText expected, ReadOnlySpan<HousingText> labels, bool requestCommits)
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
        return button == null ? Refused(action, DialogActionResult.ButtonMissing) : Press(action, addon, button, labels, requestCommits);
    }

    private static DialogActionResult Press(NativeAction action, AtkUnitBase* addon, AtkComponentButton* button, ReadOnlySpan<HousingText> expectedLabels, bool requestCommits)
    {
        if (!LabelledButtons.MayPress(button, expectedLabels, requestCommits))
        {
            RunLog.Warning($"Housing dialog: {action} refused; the button reads '{LabelledButtons.Label(button)}', not the expected label.");
            return DialogActionResult.LabelMismatch;
        }

        if (!EzThrottler.Throttle(ClickThrottleKey, ClickThrottleMs))
        {
            return DialogActionResult.Throttled;
        }

        var result = ButtonPress.Press(addon, button);
        RunLog.Info($"Housing dialog: {action} {(result == DialogActionResult.Pressed ? "pressed" : $"refused ({result})")}.");
        return result;
    }

    private static DialogActionResult Refused(NativeAction action, DialogActionResult result)
    {
        RunLog.Info($"Housing dialog: {action} refused ({result}).");
        return result;
    }
}
