namespace AutoHouseLottery.Tests.Game;

public sealed class ResultRowCrossMatchTests
{
    public static TheoryData<string, int> EveryRowInEveryLanguage()
    {
        var data = new TheoryData<string, int>();
        for (var languageIndex = 0; languageIndex < ResultRowFixtures.Languages.Length; languageIndex++)
        {
            for (var rowIndex = 0; rowIndex < ResultRowFixtures.Rows.Length; rowIndex++)
            {
                data.Add(ResultRowFixtures.Languages[languageIndex], ResultRowFixtures.Rows[rowIndex]);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryRowInEveryLanguage))]
    public void EachResultRowMatchesItsOwnDialogAsExtracted(string language, int row)
    {
        Assert.True(ResultRowFixtures.Pattern(language, row).Matches(ResultRowFixtures.Shown(language, row)));
    }

    [Theory]
    [MemberData(nameof(EveryRowInEveryLanguage))]
    public void NoResultRowMatchesAnotherResultDialog(string language, int row)
    {
        var pattern = ResultRowFixtures.Pattern(language, row);
        for (var otherIndex = 0; otherIndex < ResultRowFixtures.Rows.Length; otherIndex++)
        {
            var other = ResultRowFixtures.Rows[otherIndex];
            if (other != row)
            {
                Assert.False(pattern.Matches(ResultRowFixtures.Shown(language, other)), $"{language} row {row} matched row {other}.");
            }
        }
    }

    [Fact]
    public void ALineBreakMacroNeedsTheWhitespaceTheExtractionWrites()
    {
        var pattern = ResultRowFixtures.Pattern("en", 7122);
        var shown = ResultRowFixtures.Shown("en", 7122);
        Assert.True(pattern.Matches(shown));
        Assert.False(pattern.Matches(shown.Replace("\r\n", string.Empty, StringComparison.Ordinal)));
    }
}
