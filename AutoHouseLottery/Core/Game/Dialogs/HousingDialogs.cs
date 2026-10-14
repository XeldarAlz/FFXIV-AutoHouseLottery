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

    private static readonly HousingText[] confirmLabel = [HousingText.ConfirmLabel];
    private static readonly HousingText[] proceedLabel = [HousingText.ProceedCheckbox];

    // Every press shares one throttle, so a press right after another is refused as Throttled. A caller about to make
    // a press that must not be refused waits until this is true.
    public static bool ClickReady() => EzThrottler.Check(ClickThrottleKey);

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

    // The confirmation as it stands: whether its text is row 7046, the deposit it names, the checkbox and Confirm.
    public static EntryConfirmationReading? ReadEntryConfirmation()
    {
        if (!AddonReads.TryGetReady(EntryConfirmationAddon, out var addon))
        {
            return null;
        }

        var identified = false;
        long? deposit = null;
        if (addon->AtkValues != null)
        {
            var pattern = HousingSheetText.Pattern(HousingText.EntryConfirmation);
            for (var valueIndex = 0; valueIndex < addon->AtkValuesCount && !identified; valueIndex++)
            {
                ref readonly var value = ref addon->AtkValues[valueIndex];
                if (!AddonReads.IsText(value) || !pattern.TryCapture(AddonReads.Text(value), out var gaps))
                {
                    continue;
                }

                identified = true;
                deposit = EntryConfirmationText.Deposit(gaps);
            }
        }

        var checkBox = LabelledButtons.FindCheckBox(addon, proceedLabel);
        var proceed = checkBox == null ? CheckBoxState.Missing : checkBox->IsChecked ? CheckBoxState.Checked : CheckBoxState.Unchecked;
        var confirm = LabelledButtons.Find(addon, confirmLabel);
        return new EntryConfirmationReading(identified, deposit, proceed, confirm != null, confirm != null && confirm->IsEnabled);
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

    // Ticks "I understand and wish to proceed." (Addon 7047), the checkbox that unlocks Confirm. It commits nothing by
    // itself, but it is only ever permitted on the path to a real entry.
    public static DialogActionResult TickEntryCheckbox()
    {
        const NativeAction action = NativeAction.TickEntryCheckbox;
        if (!NativeActionGate.IsPermitted(action))
        {
            return Refused(action, DialogActionResult.NotPermitted);
        }

        if (!OpenEntryConfirmation(out var addon))
        {
            return Refused(action, DialogActionResult.IdentityMismatch);
        }

        var checkBox = LabelledButtons.FindCheckBox(addon, proceedLabel);
        if (checkBox == null)
        {
            return Refused(action, DialogActionResult.ButtonMissing);
        }

        return checkBox->IsChecked ? DialogActionResult.AlreadyChecked : Press(action, addon, checkBox, proceedLabel, requestCommits: false);
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

    // The placard only shows a plot's details, so closing it needs no permission.
    public static bool ClosePlacard()
    {
        if (!AddonReads.TryGetReady(PlacardReader.AddonName, out var addon))
        {
            return false;
        }

        addon->Close(FireCloseCallback);
        return true;
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
