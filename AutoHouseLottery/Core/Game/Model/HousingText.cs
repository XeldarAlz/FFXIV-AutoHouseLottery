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
    ConfirmLabel,
    ResultWon,
    ResultForfeited,
    ResultLost,
    RefundExpired,
    ViewResultsButton,
    ResultSoleEntrantWon,
    YesLabel,
    YesLabelAlternate,
    NoLabel,
    NoLabelAlternate,
    LotteryEntryPeriod,
    LotteryResultsPeriod,
    LotteryEntryAddress,
    LotteryNumber,
    LotteryEntryType,
    CaretakerItemsUnclaimed,
    LandOwnedOnAccount,
    OneEntryPerPeriod,
    OutstandingPropertyOrRefund,
    ProceedCheckbox,
    PurchaseBlockedAfterDemolition,
    FreeCompanyMembershipTooRecent,
    HousingActionFailed,
    FreeCompanyLeft,
}

internal readonly record struct HousingTextRow(HousingText Text, HousingSheet Sheet, uint RowId);

// Row IDs from the game's Addon and LogMessage sheets (ffxiv-datamining). Only the IDs are kept; the text is read from
// the client in its own language. Yes and No have two candidate rows each until a test in game shows which one the
// result dialog uses; both mean the same in every client language.
internal static class HousingTextRows
{
    public static readonly ImmutableArray<HousingTextRow> All =
    [
        new(HousingText.EnterLotteryButton, HousingSheet.Addon, 7045),
        new(HousingText.EntryConfirmation, HousingSheet.Addon, 7046),
        new(HousingText.ConfirmLabel, HousingSheet.Addon, 7048),
        new(HousingText.ResultWon, HousingSheet.Addon, 7122),
        new(HousingText.ResultForfeited, HousingSheet.Addon, 7123),
        new(HousingText.ResultLost, HousingSheet.Addon, 7124),
        new(HousingText.RefundExpired, HousingSheet.Addon, 7125),
        new(HousingText.ViewResultsButton, HousingSheet.Addon, 7127),
        new(HousingText.ResultSoleEntrantWon, HousingSheet.Addon, 7128),
        new(HousingText.YesLabel, HousingSheet.Addon, 3),
        new(HousingText.YesLabelAlternate, HousingSheet.Addon, 576),
        new(HousingText.NoLabel, HousingSheet.Addon, 4),
        new(HousingText.NoLabelAlternate, HousingSheet.Addon, 577),
        new(HousingText.LotteryEntryPeriod, HousingSheet.Addon, 8844),
        new(HousingText.LotteryResultsPeriod, HousingSheet.Addon, 8845),
        new(HousingText.LotteryEntryAddress, HousingSheet.Addon, 8847),
        new(HousingText.LotteryNumber, HousingSheet.Addon, 8848),
        new(HousingText.LotteryEntryType, HousingSheet.Addon, 8849),
        new(HousingText.CaretakerItemsUnclaimed, HousingSheet.LogMessage, 3420),
        new(HousingText.LandOwnedOnAccount, HousingSheet.LogMessage, 3497),
        new(HousingText.OneEntryPerPeriod, HousingSheet.LogMessage, 6099),
        new(HousingText.OutstandingPropertyOrRefund, HousingSheet.LogMessage, 6100),
        new(HousingText.ProceedCheckbox, HousingSheet.Addon, 7047),
        new(HousingText.PurchaseBlockedAfterDemolition, HousingSheet.LogMessage, 6097),
        new(HousingText.FreeCompanyMembershipTooRecent, HousingSheet.LogMessage, 6098),
        new(HousingText.HousingActionFailed, HousingSheet.LogMessage, 6101),
        new(HousingText.FreeCompanyLeft, HousingSheet.LogMessage, 6102),
    ];

    // The log messages with which the game refuses a lottery entry. Each one is about the character, not the plot, so
    // none of them is a reason to try another plot.
    public static readonly ImmutableArray<HousingText> EntryRejections =
    [
        HousingText.CaretakerItemsUnclaimed,
        HousingText.LandOwnedOnAccount,
        HousingText.OneEntryPerPeriod,
        HousingText.OutstandingPropertyOrRefund,
        HousingText.PurchaseBlockedAfterDemolition,
        HousingText.FreeCompanyMembershipTooRecent,
        HousingText.HousingActionFailed,
        HousingText.FreeCompanyLeft,
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

    public static readonly ImmutableArray<HousingText> YesLabels = [HousingText.YesLabel, HousingText.YesLabelAlternate];

    public static readonly ImmutableArray<HousingText> NoLabels = [HousingText.NoLabel, HousingText.NoLabelAlternate];

    // Labels of buttons that spend gil or take a plot. In German, French and Japanese, Confirm reads the same as Yes.
    public static readonly ImmutableArray<HousingText> CommittingLabels = [HousingText.YesLabel, HousingText.YesLabelAlternate, HousingText.ConfirmLabel];

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

internal static class ButtonLabelRule
{
    // A button is pressed only when its own label reads as one of the expected rows. A press that commits nothing
    // must also not land on a button whose label reads as a committing one, so a moved or relabelled button cannot
    // turn No into Yes. A committing press may share its label with another committing row (German, French and
    // Japanese Confirm and Yes are the same word); which of the two it is comes from the dialog's own row.
    public static bool MayPress(ReadOnlySpan<HousingText> expected, bool requestCommits, Func<HousingText, bool> labelMatches)
    {
        if (!AnyMatches(expected, labelMatches))
        {
            return false;
        }

        return requestCommits || !AnyMatches(HousingTextRows.CommittingLabels.AsSpan(), labelMatches);
    }

    private static bool AnyMatches(ReadOnlySpan<HousingText> labels, Func<HousingText, bool> labelMatches)
    {
        for (var labelIndex = 0; labelIndex < labels.Length; labelIndex++)
        {
            if (labelMatches(labels[labelIndex]))
            {
                return true;
            }
        }

        return false;
    }
}
