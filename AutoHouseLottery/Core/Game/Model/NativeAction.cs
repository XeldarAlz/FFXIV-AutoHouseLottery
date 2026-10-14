namespace AutoHouseLottery.Core.Game.Model;

// Each click the plugin can make in a housing window. None is permitted unless a caller permits it for the call.
[Flags]
internal enum NativeAction : byte
{
    None = 0,
    PressEnterLottery = 1,
    PressViewResults = 2,
    ConfirmEntry = 4,
    CancelEntry = 8,
    AcceptResult = 16,
    DeclineResult = 32,
    TickEntryCheckbox = 64,
}

internal enum DialogActionResult : byte
{
    Pressed,
    Closed,
    NotPermitted,
    WindowNotOpen,
    IdentityMismatch,
    ButtonMissing,
    LabelMismatch,
    ButtonDisabled,
    Throttled,
    AlreadyChecked,
}

// Text is the housing row the dialog was matched to, null when it matched none.
internal readonly record struct DialogIdentity(string AddonName, HousingText? Text, string ShownText);

// Sequence orders the message against the run's own presses on the shared GameSequence counter.
internal readonly record struct HousingLogObservation(HousingText Text, uint RowId, int? FirstNumber, DateTime AtUtc, long Sequence);

internal enum CheckBoxState : byte
{
    Missing,
    Unchecked,
    Checked,
}

// A checkbox of a housing window, found by its own label like the buttons.
internal readonly record struct WindowCheckBox(uint NodeId, string Text, bool Enabled, bool Visible, bool Checked, HousingText? MatchedText);

// What the open entry confirmation shows. Identified is false when its text does not read as Addon row 7046; Deposit is
// null when the amount could not be read from it.
internal readonly record struct EntryConfirmationReading(bool Identified, long? Deposit, CheckBoxState Proceed, bool ConfirmFound, bool ConfirmEnabled);
