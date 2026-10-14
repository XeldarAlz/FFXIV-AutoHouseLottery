using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Game.Model;

// What the open result dialog shows. Row is the one result row its text matched, null when it matched none or more than
// one; Refund is the amount a refund row names, null for any other row or when the amount could not be read;
// RefundDeadline is the wall-clock deadline a refund row names, null when it could not be read.
internal readonly record struct ResultDialogReading(HousingText? Row, long? Refund, DateTime? RefundDeadline, string ShownText);

internal static class ResultDialogText
{
    // Addon row 7124 fills three macros in English, German, French and Japanese alike: the winning number, the refund and
    // the refund deadline. Row 7123 fills two: the refund and the deadline. A row that shows a different number of them
    // is not the row the amount was located in, so nothing is read from it.
    private const int LostGapCount = 3;
    private const int LostRefundGap = 1;
    private const int LostDeadlineGap = 2;
    private const int ForfeitGapCount = 2;
    private const int ForfeitRefundGap = 0;
    private const int ForfeitDeadlineGap = 1;

    public static long? Refund(HousingText row, ImmutableArray<string> gaps) => row switch
    {
        HousingText.ResultLost => AmountAt(gaps, LostGapCount, LostRefundGap),
        HousingText.ResultForfeited => AmountAt(gaps, ForfeitGapCount, ForfeitRefundGap),
        _ => null,
    };

    public static DateTime? RefundDeadline(HousingText row, ImmutableArray<string> gaps, DateOrder order) => row switch
    {
        HousingText.ResultLost when HasGaps(gaps, LostGapCount) => DisplayDates.Parse(gaps[LostDeadlineGap], order),
        HousingText.ResultForfeited when HasGaps(gaps, ForfeitGapCount) => DisplayDates.Parse(gaps[ForfeitDeadlineGap], order),
        _ => null,
    };

    private static bool HasGaps(ImmutableArray<string> gaps, int expectedCount) => !gaps.IsDefault && gaps.Length == expectedCount;

    private static long? AmountAt(ImmutableArray<string> gaps, int expectedCount, int amountIndex)
    {
        if (!HasGaps(gaps, expectedCount))
        {
            return null;
        }

        return DisplayNumbers.Single([gaps[amountIndex]]) is { } amount and > 0 ? amount : null;
    }
}
