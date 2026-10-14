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
}

// Text is the housing row the dialog was matched to, null when it matched none.
internal readonly record struct DialogIdentity(string AddonName, HousingText? Text, string ShownText);

internal readonly record struct HousingLogObservation(HousingText Text, uint RowId, int? FirstNumber, DateTime AtUtc);
