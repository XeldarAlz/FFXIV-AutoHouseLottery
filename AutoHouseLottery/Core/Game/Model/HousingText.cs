using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Game.Model;

internal enum HousingSheet : byte
{
    Addon,
    LogMessage,
}

// Every housing text the plugin recognises, in the same order as HousingTextRows.All.
internal enum HousingText : byte
{
    EnterLotteryButton,
    EntryConfirmation,
    ResultWon,
    ResultForfeited,
    ResultLost,
    RefundExpired,
    ViewResultsButton,
    ResultSoleEntrantWon,
    SoleParticipant,
    CaretakerItemsUnclaimed,
    LandOwnedOnAccount,
    OneEntryPerPeriod,
    OutstandingPropertyOrRefund,
}

internal readonly record struct HousingTextRow(HousingText Text, HousingSheet Sheet, uint RowId);

// Row IDs from the game's Addon and LogMessage sheets (ffxiv-datamining). Only the IDs are kept; the text is read from
// the client in its own language.
internal static class HousingTextRows
{
    public static readonly ImmutableArray<HousingTextRow> All =
    [
        new(HousingText.EnterLotteryButton, HousingSheet.Addon, 7045),
        new(HousingText.EntryConfirmation, HousingSheet.Addon, 7046),
        new(HousingText.ResultWon, HousingSheet.Addon, 7122),
        new(HousingText.ResultForfeited, HousingSheet.Addon, 7123),
        new(HousingText.ResultLost, HousingSheet.Addon, 7124),
        new(HousingText.RefundExpired, HousingSheet.Addon, 7125),
        new(HousingText.ViewResultsButton, HousingSheet.Addon, 7127),
        new(HousingText.ResultSoleEntrantWon, HousingSheet.Addon, 7128),
        new(HousingText.SoleParticipant, HousingSheet.Addon, 7129),
        new(HousingText.CaretakerItemsUnclaimed, HousingSheet.LogMessage, 3420),
        new(HousingText.LandOwnedOnAccount, HousingSheet.LogMessage, 3497),
        new(HousingText.OneEntryPerPeriod, HousingSheet.LogMessage, 6099),
        new(HousingText.OutstandingPropertyOrRefund, HousingSheet.LogMessage, 6100),
    ];

    // The texts a SelectYesno can carry after View Lottery Results.
    public static readonly ImmutableArray<HousingText> ResultDialogs =
    [
        HousingText.ResultWon,
        HousingText.ResultForfeited,
        HousingText.ResultLost,
        HousingText.RefundExpired,
        HousingText.ResultSoleEntrantWon,
    ];

    public static HousingTextRow Of(HousingText text) => All[(int)text];

    public static bool TryFindLogMessage(uint rowId, out HousingText text)
    {
        for (var rowIndex = 0; rowIndex < All.Length; rowIndex++)
        {
            var row = All[rowIndex];
            if (row.Sheet == HousingSheet.LogMessage && row.RowId == rowId)
            {
                text = row.Text;
                return true;
            }
        }

        text = default;
        return false;
    }
}
