using AutoHouseLottery.Core.Game.Model;

namespace AutoHouseLottery.Core.Tasks.Model;

internal enum ReceiptVerdict : byte { Confirmed, Rejected, Uncertain }

// Everything seen after Confirm. Reading is the character's lottery reading refreshed after the press, null when no
// fresh one could be had, and SecondReading one refreshed again after a further delay; each NamesPlot says whether that
// reading names the entered plot (null when it cannot say). GilAfter is the latest gil read.
internal readonly record struct ReceiptFacts(
    long Deposit,
    long GilBefore,
    long? GilAfter,
    HousingText? Rejection,
    LotterySnapshot? Reading,
    bool? ReadingNamesPlot)
{
    public LotterySnapshot? SecondReading { get; init; }

    public bool? SecondReadingNamesPlot { get; init; }
}

internal readonly record struct ReceiptResult(ReceiptVerdict Verdict, uint? LotteryNumber, HousingText? Rejection);

internal static class ReceiptCorrelation
{
    // Confirmed needs a refreshed reading to show an entry on this plot and the gil to have dropped by exactly the
    // deposit; gil alone never confirms. Rejected needs the gil untouched and either the game saying no, or two fresh
    // readings, the second taken after a further delay, that both show no entry or an entry elsewhere: one reading so
    // soon after the press may not have caught up. Anything else is a question for reconciliation.
    public static ReceiptResult Correlate(in ReceiptFacts facts)
    {
        var depositTaken = facts.GilAfter is { } after && facts.GilBefore - after == facts.Deposit;
        var number = ShowsEntryNumber(facts.Reading, facts.ReadingNamesPlot) ?? ShowsEntryNumber(facts.SecondReading, facts.SecondReadingNamesPlot);
        if (depositTaken && number is not null)
        {
            return new ReceiptResult(ReceiptVerdict.Confirmed, number, null);
        }

        if (facts.GilAfter != facts.GilBefore)
        {
            return new ReceiptResult(ReceiptVerdict.Uncertain, null, facts.Rejection);
        }

        if (facts.Rejection is { } rejection)
        {
            return new ReceiptResult(ReceiptVerdict.Rejected, null, rejection);
        }

        return ShowsNoEntry(facts.Reading, facts.ReadingNamesPlot) && ShowsNoEntry(facts.SecondReading, facts.SecondReadingNamesPlot)
            ? new ReceiptResult(ReceiptVerdict.Rejected, null, null)
            : new ReceiptResult(ReceiptVerdict.Uncertain, null, null);
    }

    // The gil is untouched, the game said nothing, and the one reading so far shows no entry on this plot: a second
    // reading after a further delay is what can still release the slot.
    public static bool NeedsSecondReading(in ReceiptFacts facts)
        => facts.SecondReading is null
            && facts.Rejection is null
            && facts.GilAfter == facts.GilBefore
            && ShowsNoEntry(facts.Reading, facts.ReadingNamesPlot);

    private static uint? ShowsEntryNumber(LotterySnapshot? reading, bool? namesPlot)
        => reading is { State: LotterySnapshotState.Active, Entry.LotteryNumber: { } number } && namesPlot == true ? number : null;

    private static bool ShowsNoEntry(LotterySnapshot? reading, bool? namesPlot)
        => reading is { State: LotterySnapshotState.LoadedNone } || (reading is { State: LotterySnapshotState.Active } && namesPlot == false);
}
