using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Travel.Model;

namespace AutoHouseLottery.Core.Settlement.Model;

// What one visit to the entered plot established. ResultsOffered: the placard offered View Lottery Results to this
// character. OfferedRefund: the amount a refund dialog named, null when it named none or could not be read. Recorded and
// RecordedRefund: what the journal holds for the receipt. OpenAttempt: the kind of this plugin's own refund or claim
// still open on the receipt, null when there is none. ReadingShowsEntry: a fresh lottery reading during the receipt's
// results period, true when it still shows this entry, false when it does not, null when it cannot say.
// OwnsPlot: the character's own estate list holds this plot. RefundWindowPassed: the estimated 90-day refund window
// has closed.
internal readonly record struct InspectionFacts(
    PlacardIdentityVerdict Identity,
    bool ResultsOffered,
    ResultKind Result,
    long? OfferedRefund,
    SettlementState Recorded,
    RefundKind RecordedRefund,
    ActionKind? OpenAttempt,
    bool? ReadingShowsEntry,
    bool OwnsPlot,
    bool RefundWindowPassed);

internal enum InspectionOutcome : byte { OutcomeUnknown, WonUnclaimed, LostRefundDue, ForfeitRefundDue, Expired, Claimed, Refunded }

internal enum InspectionReason : byte
{
    None,
    WrongPlacard,
    PlacardUnverified,
    NoResultOffered,
    ResultNotShown,
    UnrecognizedDialog,
    AmountUnreadable,
    ContradictsRecord,
}

// Refund is the amount the game offers for a refund outcome; it stays null for a settlement seen from outside, whose
// amount was never shown.
internal readonly record struct InspectionVerdict(InspectionOutcome Outcome, long? Refund, InspectionReason Reason)
{
    public static InspectionVerdict Unknown(InspectionReason reason) => new(InspectionOutcome.OutcomeUnknown, null, reason);
}

// What the plot's result says about an earlier cycle's entry attempt still in question: the press entered the lottery,
// or it did not, or the visit cannot tell.
internal enum AttemptVerdict : byte { Unknown, Entered, NotEntered }

internal static class InspectionRules
{
    // Any result row shown to this character at the attempted plot, or the plot in its estate list, proves the entry was
    // made. A confirmed placard that no longer offers this character any result proves it was not, since that cycle's
    // lottery reading is gone and the placard is the only witness left. Anything else leaves the attempt in question.
    public static AttemptVerdict DecideAttempt(PlacardIdentityVerdict identity, bool resultsOffered, ResultKind result, bool ownsPlot)
    {
        if (identity != PlacardIdentityVerdict.Confirmed)
        {
            return AttemptVerdict.Unknown;
        }

        if (ownsPlot || result is ResultKind.Won or ResultKind.SoleEntrantWon or ResultKind.Lost or ResultKind.Forfeited or ResultKind.Expired)
        {
            return AttemptVerdict.Entered;
        }

        return result == ResultKind.NotShown && !resultsOffered ? AttemptVerdict.NotEntered : AttemptVerdict.Unknown;
    }

    // The result dialog's own row decides; inspection never infers a loss from anything else, and only ever runs once the
    // results are out. Without a dialog the entry counts as settled outside the visit: claimed when the plot is in the
    // character's estate list, otherwise as below. A result that contradicts what is already recorded is reported, never
    // written over it.
    public static InspectionVerdict Decide(in InspectionFacts facts)
    {
        switch (facts.Identity)
        {
            case PlacardIdentityVerdict.WrongPlot:
                return InspectionVerdict.Unknown(InspectionReason.WrongPlacard);
            case PlacardIdentityVerdict.Unverified:
                return InspectionVerdict.Unknown(InspectionReason.PlacardUnverified);
        }

        var refundRecorded = SettlementStates.OnRefundPath(facts.Recorded) || facts.OpenAttempt == ActionKind.Refund;
        if (facts.OwnsPlot && !refundRecorded)
        {
            return new InspectionVerdict(InspectionOutcome.Claimed, null, InspectionReason.None);
        }

        switch (facts.Result)
        {
            case ResultKind.Won or ResultKind.SoleEntrantWon:
                return refundRecorded ? InspectionVerdict.Unknown(InspectionReason.ContradictsRecord) : new InspectionVerdict(InspectionOutcome.WonUnclaimed, null, InspectionReason.None);
            case ResultKind.Lost:
                return Refund(facts, InspectionOutcome.LostRefundDue, RefundKind.Loss);
            case ResultKind.Forfeited:
                return Refund(facts, InspectionOutcome.ForfeitRefundDue, RefundKind.Forfeit);
            case ResultKind.Expired:
                return new InspectionVerdict(InspectionOutcome.Expired, null, InspectionReason.None);
            case ResultKind.Unrecognized:
                return InspectionVerdict.Unknown(InspectionReason.UnrecognizedDialog);
        }

        return facts.ResultsOffered ? InspectionVerdict.Unknown(InspectionReason.ResultNotShown) : NoResultOffered(facts);
    }

    private static InspectionVerdict Refund(in InspectionFacts facts, InspectionOutcome outcome, RefundKind kind)
    {
        if (facts.OfferedRefund is not { } amount)
        {
            return InspectionVerdict.Unknown(InspectionReason.AmountUnreadable);
        }

        var otherRefund = facts.RecordedRefund != RefundKind.None && facts.RecordedRefund != kind;
        var lossAfterWin = kind == RefundKind.Loss && SettlementStates.OnWinPath(facts.Recorded);
        return otherRefund || lossAfterWin || facts.OpenAttempt == ActionKind.Claim
            ? InspectionVerdict.Unknown(InspectionReason.ContradictsRecord)
            : new InspectionVerdict(outcome, amount, InspectionReason.None);
    }

    // Once the results are out the game offers each entrant its result until it is settled, so a confirmed placard that
    // offers this character none any more means it was settled outside this visit: claimed when the plot is in the estate
    // list (decided above), lapsed when the refund window has closed, otherwise refunded, for an amount nobody saw. The
    // placard is the only witness left for an earlier cycle, the same as for an uncertain attempt. A reading that still
    // shows the entry, or a claim of this plugin still in question, leaves it open.
    private static InspectionVerdict NoResultOffered(in InspectionFacts facts)
    {
        if (facts.OwnsPlot)
        {
            return InspectionVerdict.Unknown(InspectionReason.ContradictsRecord);
        }

        if (facts.ReadingShowsEntry == true || facts.OpenAttempt == ActionKind.Claim || facts.Recorded == SettlementState.ClaimPending)
        {
            return InspectionVerdict.Unknown(InspectionReason.NoResultOffered);
        }

        var lapsed = facts.RefundWindowPassed && facts.OpenAttempt != ActionKind.Refund;
        return new InspectionVerdict(lapsed ? InspectionOutcome.Expired : InspectionOutcome.Refunded, null, InspectionReason.None);
    }

    public static SettlementState StateOf(InspectionOutcome outcome) => outcome switch
    {
        InspectionOutcome.WonUnclaimed => SettlementState.WonUnclaimed,
        InspectionOutcome.LostRefundDue => SettlementState.LostRefundDue,
        InspectionOutcome.ForfeitRefundDue => SettlementState.ForfeitRefundDue,
        InspectionOutcome.Expired => SettlementState.Expired,
        InspectionOutcome.Claimed => SettlementState.Claimed,
        InspectionOutcome.Refunded => SettlementState.Refunded,
        _ => SettlementState.OutcomeUnknown,
    };
}
