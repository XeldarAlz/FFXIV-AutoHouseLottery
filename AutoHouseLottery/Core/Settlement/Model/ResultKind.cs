using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Persistence.Model;

namespace AutoHouseLottery.Core.Settlement.Model;

// What View Lottery Results showed, by the result row its dialog matched. NotShown: no result dialog appeared.
// Unrecognized: a dialog appeared that matched none of the result rows.
internal enum ResultKind : byte { NotShown, Won, SoleEntrantWon, Lost, Forfeited, Expired, Unrecognized }

internal static class ResultKinds
{
    public static ResultKind Of(bool dialogShown, HousingText? row)
    {
        if (!dialogShown)
        {
            return ResultKind.NotShown;
        }

        return row switch
        {
            HousingText.ResultWon => ResultKind.Won,
            HousingText.ResultSoleEntrantWon => ResultKind.SoleEntrantWon,
            HousingText.ResultLost => ResultKind.Lost,
            HousingText.ResultForfeited => ResultKind.Forfeited,
            HousingText.RefundExpired => ResultKind.Expired,
            _ => ResultKind.Unrecognized,
        };
    }

    public static bool IsWin(ResultKind kind) => kind is ResultKind.Won or ResultKind.SoleEntrantWon;

    public static bool IsRefund(ResultKind kind) => kind is ResultKind.Lost or ResultKind.Forfeited;

    public static RefundKind RefundOf(ResultKind kind) => kind switch
    {
        ResultKind.Lost => RefundKind.Loss,
        ResultKind.Forfeited => RefundKind.Forfeit,
        _ => RefundKind.None,
    };

    // The only rows Yes may be pressed on: the refund rows for a refund, the win rows for a claim.
    public static HousingText? AcceptRow(ResultKind kind, SettlementMode mode) => (kind, mode) switch
    {
        (ResultKind.Lost, SettlementMode.Refund) => HousingText.ResultLost,
        (ResultKind.Forfeited, SettlementMode.Refund) => HousingText.ResultForfeited,
        (ResultKind.Won, SettlementMode.Claim) => HousingText.ResultWon,
        (ResultKind.SoleEntrantWon, SettlementMode.Claim) => HousingText.ResultSoleEntrantWon,
        _ => null,
    };
}
