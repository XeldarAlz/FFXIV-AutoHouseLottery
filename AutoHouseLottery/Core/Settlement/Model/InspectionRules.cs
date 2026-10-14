using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Travel.Model;

namespace AutoHouseLottery.Core.Settlement.Model;

// What one visit to the entered plot established. ResultsOffered: the placard offered View Lottery Results to this
// character. OfferedRefund: the amount a refund dialog named, null when it named none or could not be read. Recorded and
// RecordedRefund: what the journal holds for the receipt. OpenAttempt: the kind of this plugin's own refund or claim
// still open on the receipt, null when there is none. OwnsPlot: the character's own estate list holds this plot.
// RefundWindowPassed: the refund deadline (the dialog's own, or the 90-day estimate) has passed. Absence: the evidence
// that decides whether a placard without a result means the entry was settled elsewhere.
internal readonly record struct InspectionFacts(
    PlacardIdentityVerdict Identity,
    bool ResultsOffered,
    ResultKind Result,
    long? OfferedRefund,
    SettlementState Recorded,
    RefundKind RecordedRefund,
    ActionKind? OpenAttempt,
    bool OwnsPlot,
    bool RefundWindowPassed,
    AbsenceEvidence Absence);

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
// amount was never shown. ByAbsence: the outcome rests on a placard that no longer offers a result.
internal readonly record struct InspectionVerdict(InspectionOutcome Outcome, long? Refund, InspectionReason Reason)
{
    public bool ByAbsence { get; init; }

    public static InspectionVerdict Unknown(InspectionReason reason) => new(InspectionOutcome.OutcomeUnknown, null, reason);
}

// What the plot's result says about an earlier cycle's entry attempt still in question: the press entered the lottery,
// or it did not, or the visit cannot tell.
internal enum AttemptVerdict : byte { Unknown, Entered, NotEntered }

internal static class InspectionRules
{
    // Any result row shown to this character at the attempted plot, or the plot in its estate list, proves the entry was
    // made. Only the validated absence rule can prove it was not; until then the attempt stays in question.
    public static AttemptVerdict DecideAttempt(PlacardIdentityVerdict identity, bool resultsOffered, ResultKind result, bool ownsPlot, in AbsenceEvidence absence)
    {
        if (identity != PlacardIdentityVerdict.Confirmed)
        {
            return AttemptVerdict.Unknown;
        }

        if (ownsPlot || result is ResultKind.Won or ResultKind.SoleEntrantWon or ResultKind.Lost or ResultKind.Forfeited or ResultKind.Expired)
        {
            return AttemptVerdict.Entered;
        }

        return result == ResultKind.NotShown && !resultsOffered && AbsenceRules.Settles(absence, SettlementState.Entered)
            ? AttemptVerdict.NotEntered
            : AttemptVerdict.Unknown;
    }

    // The result dialog's own row decides; inspection never infers a loss from anything else, and only ever runs once the
    // results are out. Without a dialog the estate list can show a claim; a placard without any result settles nothing
    // unless the absence rule allows it. A result that contradicts what is recorded, or the estate list, is reported,
    // never written over it.
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
        switch (facts.Result)
        {
            case ResultKind.Won or ResultKind.SoleEntrantWon:
                return refundRecorded || facts.OwnsPlot
                    ? InspectionVerdict.Unknown(InspectionReason.ContradictsRecord)
                    : new InspectionVerdict(InspectionOutcome.WonUnclaimed, null, InspectionReason.None);
            case ResultKind.Lost:
                return Refund(facts, InspectionOutcome.LostRefundDue, RefundKind.Loss);
            case ResultKind.Forfeited:
                return Refund(facts, InspectionOutcome.ForfeitRefundDue, RefundKind.Forfeit);
            case ResultKind.Expired:
                return facts.OwnsPlot ? InspectionVerdict.Unknown(InspectionReason.ContradictsRecord) : new InspectionVerdict(InspectionOutcome.Expired, null, InspectionReason.None);
            case ResultKind.Unrecognized:
                return InspectionVerdict.Unknown(InspectionReason.UnrecognizedDialog);
        }

        if (facts.OwnsPlot)
        {
            return refundRecorded ? InspectionVerdict.Unknown(InspectionReason.ContradictsRecord) : new InspectionVerdict(InspectionOutcome.Claimed, null, InspectionReason.None);
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
        return otherRefund || lossAfterWin || facts.OwnsPlot || facts.OpenAttempt == ActionKind.Claim
            ? InspectionVerdict.Unknown(InspectionReason.ContradictsRecord)
            : new InspectionVerdict(outcome, amount, InspectionReason.None);
    }

    // Lapsed when the refund window has closed, otherwise refunded, for an amount nobody saw; never while a claim of this
    // plugin may still be going through.
    private static InspectionVerdict NoResultOffered(in InspectionFacts facts)
    {
        if (facts.OpenAttempt == ActionKind.Claim || facts.Recorded == SettlementState.ClaimPending || !AbsenceRules.Settles(facts.Absence, facts.Recorded))
        {
            return InspectionVerdict.Unknown(InspectionReason.NoResultOffered);
        }

        var lapsed = facts.RefundWindowPassed && facts.OpenAttempt != ActionKind.Refund;
        return new InspectionVerdict(lapsed ? InspectionOutcome.Expired : InspectionOutcome.Refunded, null, InspectionReason.None) { ByAbsence = true };
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
