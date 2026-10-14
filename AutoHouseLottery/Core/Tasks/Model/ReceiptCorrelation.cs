using AutoHouseLottery.Core.Game.Model;

namespace AutoHouseLottery.Core.Tasks.Model;

internal enum ReceiptVerdict : byte { Confirmed, Rejected, Uncertain }

// Everything seen after Confirm. Reading is the character's lottery reading refreshed after the press, null when no
// fresh one could be had; ReadingNamesPlot is whether it names the entered plot (null when it cannot say).
internal readonly record struct ReceiptFacts(
    long Deposit,
    long GilBefore,
    long? GilAfter,
    HousingText? Rejection,
    LotterySnapshot? Reading,
    bool? ReadingNamesPlot);

internal readonly record struct ReceiptResult(ReceiptVerdict Verdict, uint? LotteryNumber, HousingText? Rejection);

internal static class ReceiptCorrelation
{
    // Confirmed needs the refreshed reading to show an entry on this plot and the gil to have dropped by exactly the
    // deposit; gil alone never confirms. Rejected needs the gil untouched and the game saying no, or a reading that
    // shows no entry or an entry elsewhere. Anything else is a question for reconciliation.
    public static ReceiptResult Correlate(in ReceiptFacts facts)
    {
        var gilUntouched = facts.GilAfter == facts.GilBefore;
        var depositTaken = facts.GilAfter is { } after && facts.GilBefore - after == facts.Deposit;
        if (facts.Reading is { State: LotterySnapshotState.Active, Entry.LotteryNumber: { } number } && facts.ReadingNamesPlot == true && depositTaken)
        {
            return new ReceiptResult(ReceiptVerdict.Confirmed, number, null);
        }

        if (!gilUntouched)
        {
            return new ReceiptResult(ReceiptVerdict.Uncertain, null, facts.Rejection);
        }

        if (facts.Rejection is { } rejection)
        {
            return new ReceiptResult(ReceiptVerdict.Rejected, null, rejection);
        }

        var shownElsewhere = facts.Reading is { State: LotterySnapshotState.Active } && facts.ReadingNamesPlot == false;
        var shownNone = facts.Reading is { State: LotterySnapshotState.LoadedNone };
        return shownElsewhere || shownNone
            ? new ReceiptResult(ReceiptVerdict.Rejected, null, null)
            : new ReceiptResult(ReceiptVerdict.Uncertain, null, null);
    }
}
