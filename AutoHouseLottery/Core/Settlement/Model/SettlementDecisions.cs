using AutoHouseLottery.Core.Persistence.Model;

namespace AutoHouseLottery.Core.Settlement.Model;

internal enum VisitAction : byte { Close, CollectRefund, Claim }

internal enum AttemptResolution : byte { Keep, Confirm, Release }

// The decisions a settlement visit makes after reading the result, apart from the game.
internal static class SettlementDecisions
{
    // Yes is reached only when the mode, the row and the recorded verdict all agree: a refund for a loss or forfeit row
    // that the verdict records as owed with its amount, a claim for a win row the verdict records as won. A visit that
    // found its own refund or claim still open only reads it back; everything else closes the dialog.
    public static VisitAction Act(SettlementMode mode, ResultKind result, in InspectionVerdict verdict, bool attemptOpenBeforeVisit)
    {
        if (attemptOpenBeforeVisit)
        {
            return VisitAction.Close;
        }

        return (mode, result, verdict.Outcome) switch
        {
            (SettlementMode.Refund, ResultKind.Lost, InspectionOutcome.LostRefundDue) when verdict.Refund is not null => VisitAction.CollectRefund,
            (SettlementMode.Refund, ResultKind.Forfeited, InspectionOutcome.ForfeitRefundDue) when verdict.Refund is not null => VisitAction.CollectRefund,
            (SettlementMode.Claim, ResultKind.Won or ResultKind.SoleEntrantWon, InspectionOutcome.WonUnclaimed) => VisitAction.Claim,
            _ => VisitAction.Close,
        };
    }

    // The plugin's own refund or claim still in question, settled by what the plot shows now: the same result offered
    // again means the press never went through; the settled state means it did. Anything else keeps it in question.
    public static AttemptResolution ResolveOpenAttempt(ActionKind kind, InspectionOutcome outcome) => (kind, outcome) switch
    {
        (ActionKind.Refund, InspectionOutcome.Refunded) or (ActionKind.Claim, InspectionOutcome.Claimed) => AttemptResolution.Confirm,
        (ActionKind.Refund, InspectionOutcome.LostRefundDue or InspectionOutcome.ForfeitRefundDue) => AttemptResolution.Release,
        (ActionKind.Claim, InspectionOutcome.WonUnclaimed) => AttemptResolution.Release,
        _ => AttemptResolution.Keep,
    };

    // A plot read from the Timers text is kept only once the game proved it: a result row shown to this character there,
    // or the plot in its estate list. A placard that merely offered nothing proves nothing about the address.
    public static bool ProvesPlot(ResultKind result, bool ownsPlot)
        => ownsPlot || result is ResultKind.Won or ResultKind.SoleEntrantWon or ResultKind.Lost or ResultKind.Forfeited or ResultKind.Expired;

    // An attempt the journal still holds as Prepared was never recorded as dispatched, so it is first set aside as in
    // question; Dispatched and NeedsReconciliation confirm directly.
    public static bool NeedsQuestionBeforeConfirm(AttemptState state) => state == AttemptState.Prepared;
}
