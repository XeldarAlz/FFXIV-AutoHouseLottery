using AutoHouseLottery.Core.Game.Model;

namespace AutoHouseLottery.Tests.Game;

public sealed class ResultDialogTextTests
{
    private const int Lost = 7124;
    private const int Forfeited = 7123;
    private const int Won = 7122;
    private const string WinningNumber = "1234";
    private const string Deadline = "2026/10/24 8:00";
    private const string NoBreakSpace = "\u00A0";
    private const string NarrowNoBreakSpace = "\u202F";

    public static TheoryData<string, string> AmountInEveryLanguageFormat()
    {
        var data = new TheoryData<string, string>();
        string[] formats = ["3,187,500", "3.187.500", "3" + NoBreakSpace + "187" + NoBreakSpace + "500", "3" + NarrowNoBreakSpace + "187" + NarrowNoBreakSpace + "500", "3 187 500", "3187500"];
        for (var languageIndex = 0; languageIndex < ResultRowFixtures.Languages.Length; languageIndex++)
        {
            for (var formatIndex = 0; formatIndex < formats.Length; formatIndex++)
            {
                data.Add(ResultRowFixtures.Languages[languageIndex], formats[formatIndex]);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AmountInEveryLanguageFormat))]
    public void LostRefundIsTheSecondAmountNotTheWinningNumber(string language, string amount)
    {
        Assert.Equal(3_187_500, Refund(language, Lost, HousingText.ResultLost, WinningNumber, amount, Deadline));
    }

    [Theory]
    [MemberData(nameof(AmountInEveryLanguageFormat))]
    public void ForfeitRefundIsTheFirstAmount(string language, string amount)
    {
        Assert.Equal(3_187_500, Refund(language, Forfeited, HousingText.ResultForfeited, amount, Deadline));
    }

    [Theory]
    [InlineData("31,87,500")]
    [InlineData("3,1875,00")]
    [InlineData("3,187,500 1234")]
    [InlineData("3万")]
    [InlineData("0")]
    [InlineData("３,１８７,５００")]
    [InlineData("99,999,999,999,999,999,999")]
    [InlineData("")]
    public void AnAmountThatIsNotPlainlyOneNumberIsNotRead(string amount)
    {
        for (var languageIndex = 0; languageIndex < ResultRowFixtures.Languages.Length; languageIndex++)
        {
            var language = ResultRowFixtures.Languages[languageIndex];
            Assert.Null(Refund(language, Lost, HousingText.ResultLost, WinningNumber, amount, Deadline));
            Assert.Null(Refund(language, Forfeited, HousingText.ResultForfeited, amount, Deadline));
        }
    }

    [Fact]
    public void AWinRowNamesNoRefund()
    {
        for (var languageIndex = 0; languageIndex < ResultRowFixtures.Languages.Length; languageIndex++)
        {
            var language = ResultRowFixtures.Languages[languageIndex];
            Assert.True(ResultRowFixtures.Pattern(language, Won).TryCapture(ResultRowFixtures.ShownWith(language, Won, WinningNumber, Deadline), out var gaps));
            Assert.Null(ResultDialogText.Refund(HousingText.ResultWon, gaps));
            Assert.Null(ResultDialogText.Refund(HousingText.ResultSoleEntrantWon, gaps));
        }
    }

    [Fact]
    public void AnUnexpectedNumberOfFilledInPartsReadsNoAmount()
    {
        Assert.Null(ResultDialogText.Refund(HousingText.ResultLost, ["1234", "3,187,500"]));
        Assert.Null(ResultDialogText.Refund(HousingText.ResultLost, ["1234", "3,187,500", Deadline, "extra"]));
        Assert.Null(ResultDialogText.Refund(HousingText.ResultForfeited, ["3,187,500"]));
        Assert.Null(ResultDialogText.Refund(HousingText.ResultForfeited, default));
    }

    [Fact]
    public void TheLostRowReadThroughTheForfeitRuleNeverYieldsTheWinningNumber()
    {
        var pattern = ResultRowFixtures.Pattern("en", Lost);
        Assert.True(pattern.TryCapture(ResultRowFixtures.ShownWith("en", Lost, WinningNumber, "3,187,500", Deadline), out var gaps));
        Assert.Null(ResultDialogText.Refund(HousingText.ResultForfeited, gaps));
    }

    [Theory]
    [InlineData("ja", "2026/10/24 8:00", (byte)DateOrder.YearMonthDay)]
    [InlineData("de", "24.10.2026 8:00", (byte)DateOrder.DayMonthYear)]
    [InlineData("fr", "24/10/2026 08:00", (byte)DateOrder.DayMonthYear)]
    [InlineData("en", "10/24/2026 8:00", (byte)DateOrder.MonthDayYear)]
    public void TheRefundDeadlineIsReadFromItsOwnPartInEveryLanguage(string language, string deadline, byte order)
    {
        var expected = new DateTime(2026, 10, 24, 8, 0, 0, DateTimeKind.Unspecified);

        Assert.Equal(expected, DeadlineOf(language, Lost, HousingText.ResultLost, (DateOrder)order, WinningNumber, "3,187,500", deadline));
        Assert.Equal(expected, DeadlineOf(language, Forfeited, HousingText.ResultForfeited, (DateOrder)order, "1,593,750", deadline));
    }

    [Theory]
    [InlineData("10/24/2026 8:00 a.m.")]
    [InlineData("24 October 2026 8:00")]
    [InlineData("2026年10月24日 8時00分")]
    [InlineData("10/24/26 8:00")]
    [InlineData("10/24/2026")]
    [InlineData("13/24/2026 8:00")]
    [InlineData("2/30/2026 8:00")]
    [InlineData("10/24/2026 24:00")]
    [InlineData("")]
    public void ADeadlineThatIsNotPlainlyADateAndTimeIsNotRead(string deadline)
    {
        Assert.Null(DeadlineOf("en", Lost, HousingText.ResultLost, DateOrder.MonthDayYear, WinningNumber, "3,187,500", deadline));
    }

    private static DateTime? DeadlineOf(string language, int row, HousingText text, DateOrder order, params string[] values)
    {
        var pattern = ResultRowFixtures.Pattern(language, row);
        Assert.True(pattern.TryCapture(ResultRowFixtures.ShownWith(language, row, values), out var gaps), $"{language} row {row} did not match its own dialog.");
        return ResultDialogText.RefundDeadline(text, gaps, order);
    }

    private static long? Refund(string language, int row, HousingText text, params string[] values)
    {
        var pattern = ResultRowFixtures.Pattern(language, row);
        Assert.True(pattern.TryCapture(ResultRowFixtures.ShownWith(language, row, values), out var gaps), $"{language} row {row} did not match its own dialog.");
        return ResultDialogText.Refund(text, gaps);
    }
}
