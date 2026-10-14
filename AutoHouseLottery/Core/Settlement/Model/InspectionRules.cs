using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Travel.Model;

namespace AutoHouseLottery.Core.Settlement.Model;

// What one visit to the entered plot established. ResultsOffered: the placard offered View Lottery Results to this
// character. OfferedRefund: the amount a refund dialog named, null when it named none or could not be read. Recorded and
// RecordedRefund: what the journal holds for the receipt. OpenAttempt: the kind of this plugin's own refund or claim
// still open on the receipt, null when there is none. ReadingShowsEntry: a fresh lottery reading during the receipt's
// results period, true when it shows this entry, false when it proves the entry gone, null when it cannot say.
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

    // The result dialog's own row decides; inspection never infers a loss from anything else. Without a dialog the entry
    // counts as settled only on independent evidence: the plot in the character's estate list, or the placard no longer
    // offering a result together with this plugin's own refund press or a reading that proves the entry gone. A result
    // that contradicts what is already recorded is reported, never written over it.
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

        return facts.ResultsOffered ? InspectionVerdict.Unknown(InspectionReason.ResultNotShown) : NoResultOffered(facts, refundRecorded);
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

    private static InspectionVerdict NoResultOffered(in InspectionFacts facts, bool refundRecorded)
    {
        if (refundRecorded && (facts.OpenAttempt == ActionKind.Refund || facts.ReadingShowsEntry == false))
        {
            return new InspectionVerdict(InspectionOutcome.Refunded, null, InspectionReason.None);
        }

        if (SettlementStates.OutcomeOpen(facts.Recorded) && facts.OpenAttempt is null && facts.ReadingShowsEntry == false)
        {
            var outcome = facts.RefundWindowPassed ? InspectionOutcome.Expired : InspectionOutcome.Refunded;
            return new InspectionVerdict(outcome, null, InspectionReason.None);
        }

        return InspectionVerdict.Unknown(InspectionReason.NoResultOffered);
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
