using AutoHouseLottery.Core.Game.Model;

namespace AutoHouseLottery.Tests.Game;

public sealed class SheetTextPatternTests
{
    private static readonly SheetTextToken Gap = SheetTextToken.Gap;

    // Addon 7046 in English: the deposit and the buyer wording are macros, the rest is static.
    private static readonly SheetTextPattern entryConfirmation = Pattern(
        SheetTextToken.Literal("Do you wish to deposit "),
        Gap,
        SheetTextToken.Literal(" gil to enter the lottery for this "),
        Gap,
        SheetTextToken.Literal("? Please note that you may only enter the lottery for one plot of land during each lottery period."));

    [Fact]
    public void MatchesTheRenderedEntryConfirmation()
    {
        Assert.True(entryConfirmation.Matches(
            "Do you wish to deposit 3,000,000 gil to enter the lottery for this personal plot? Please note that you may only enter the lottery for one plot of land during each lottery period."));
    }

    [Fact]
    public void MatchesAcrossLineBreaksAndSoftHyphens()
    {
        Assert.True(entryConfirmation.Matches(
            "Do you wish to deposit 3,000,000 gil\nto enter the lot­tery for this plot? Please note that you may only enter the lottery for one plot of land during each lottery period."));
    }

    [Fact]
    public void RefusesAnotherDialogThatSharesWords()
    {
        Assert.False(entryConfirmation.Matches(
            "The winning number is...123. May you have better luck in the future! Accept a full refund of your deposit of 3,000,000 gil?"));
    }

    [Fact]
    public void RefusesStaticTextInTheWrongOrder()
    {
        var pattern = Pattern(Gap, SheetTextToken.Literal("winning number"), Gap, SheetTextToken.Literal("refund"), Gap);
        Assert.True(pattern.Matches("The winning number is 7. Accept a full refund?"));
        Assert.False(pattern.Matches("Accept a full refund? The winning number is 7."));
    }

    [Fact]
    public void RefusesTextWithAPrefixBeforeAStaticStart()
    {
        Assert.False(entryConfirmation.Matches(
            "Note: Do you wish to deposit 5 gil to enter the lottery for this plot? Please note that you may only enter the lottery for one plot of land during each lottery period."));
    }

    [Fact]
    public void RefusesTextThatRunsOnPastAStaticEnd()
    {
        var pattern = Pattern(Gap, SheetTextToken.Literal(" has been sold."));
        Assert.True(pattern.Matches("Plot 12 has been sold."));
        Assert.False(pattern.Matches("Plot 12 has been sold. Try another."));
    }

    [Theory]
    [InlineData("Enter Lottery", true)]
    [InlineData("  enter   LOTTERY ", true)]
    [InlineData("Enter Lottery Now", false)]
    [InlineData("Enter", false)]
    [InlineData("", false)]
    public void AFullyStaticRowMatchesOnlyTheWholeText(string shown, bool expected)
    {
        Assert.Equal(expected, Pattern(SheetTextToken.Literal("Enter Lottery")).Matches(shown));
    }

    [Fact]
    public void ARowOfOnlyMacrosNeverMatches()
    {
        var pattern = Pattern(Gap, SheetTextToken.Literal("  "), Gap);
        Assert.False(pattern.IsUsable);
        Assert.False(pattern.Matches("anything at all"));
        Assert.False(SheetTextPattern.Empty.Matches(string.Empty));
    }

    [Fact]
    public void WhitespaceBetweenMacrosIsNotASegment()
    {
        var pattern = Pattern(SheetTextToken.Literal("Plot "), Gap, SheetTextToken.Literal(" "), Gap, SheetTextToken.Literal(" Ward"));
        string[] segments = [.. pattern.Segments];
        Assert.Equal(["Plot", "Ward"], segments);
        Assert.True(pattern.Matches("Plot 5, 12th Ward"));
    }

    [Fact]
    public void TrailingWhitespaceAfterAMacroLeavesTheEndOpen()
    {
        var pattern = Pattern(SheetTextToken.Literal("Accept the remaining refund of "), Gap, SheetTextToken.Literal("\n"));
        Assert.True(pattern.Matches("Accept the remaining refund of 1,500,000 gil?"));
    }

    [Fact]
    public void MatchesTextWithoutSpacesBetweenWords()
    {
        var pattern = Pattern(SheetTextToken.Literal("抽選に"), Gap, SheetTextToken.Literal("ギルを預けます"));
        Assert.True(pattern.Matches("抽選に3,000,000ギルを預けます"));
        Assert.False(pattern.Matches("抽選に3,000,000ギルを返します"));
    }

    [Fact]
    public void NormalizeDropsGameGlyphsAndCollapsesWhitespace()
    {
        Assert.Equal("3,000,000 gil", DisplayText.Normalize(" 3,000,000 \r\n gil​ "));
    }

    private static SheetTextPattern Pattern(params SheetTextToken[] tokens) => SheetTextPattern.FromTokens(tokens);
}
