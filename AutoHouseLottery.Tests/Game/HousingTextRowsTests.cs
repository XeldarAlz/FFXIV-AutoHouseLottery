using AutoHouseLottery.Core.Game.Model;

namespace AutoHouseLottery.Tests.Game;

public sealed class HousingTextRowsTests
{
    [Fact]
    public void EveryTextLooksUpItsOwnRow()
    {
        var texts = Enum.GetValues<HousingText>();
        Assert.Equal(texts.Length, HousingTextRows.All.Length);
        for (var textIndex = 0; textIndex < texts.Length; textIndex++)
        {
            Assert.Equal(texts[textIndex], HousingTextRows.Of(texts[textIndex]).Text);
        }
    }

    [Fact]
    public void NoTwoTextsShareASheetRow()
    {
        var seen = new HashSet<(HousingSheet, uint)>();
        for (var rowIndex = 0; rowIndex < HousingTextRows.All.Length; rowIndex++)
        {
            var row = HousingTextRows.All[rowIndex];
            Assert.True(seen.Add((row.Sheet, row.RowId)), $"{row.Sheet} {row.RowId} is listed twice.");
        }
    }

    [Theory]
    [InlineData(3420u)]
    [InlineData(3497u)]
    [InlineData(6099u)]
    [InlineData(6100u)]
    [InlineData(6097u)]
    [InlineData(6098u)]
    [InlineData(6101u)]
    [InlineData(6102u)]
    public void FindsEachRejectionByItsLogMessageRow(uint rowId)
    {
        Assert.True(HousingTextRows.TryFindLogMessage(rowId, out var text));
        Assert.Equal(HousingSheet.LogMessage, HousingTextRows.Of(text).Sheet);
    }

    [Theory]
    [InlineData(7046u)]
    [InlineData(0u)]
    [InlineData(6096u)]
    public void AnAddonRowOrUnknownIdIsNoRejection(uint rowId)
    {
        Assert.False(HousingTextRows.TryFindLogMessage(rowId, out _));
    }

    [Fact]
    public void EveryEntryRejectionIsALogMessageAndTheCheckboxIsAnAddonRow()
    {
        for (var index = 0; index < HousingTextRows.EntryRejections.Length; index++)
        {
            Assert.Equal(HousingSheet.LogMessage, HousingTextRows.Of(HousingTextRows.EntryRejections[index]).Sheet);
        }

        Assert.Equal(new HousingTextRow(HousingText.ProceedCheckbox, HousingSheet.Addon, 7047), HousingTextRows.Of(HousingText.ProceedCheckbox));
    }

    [Fact]
    public void TheCheckboxIsNoCommittingLabel()
    {
        Assert.True(ButtonLabelRule.MayPress([HousingText.ProceedCheckbox], requestCommits: false, LabelReads(HousingText.ProceedCheckbox)));
        Assert.False(ButtonLabelRule.MayPress([HousingText.ProceedCheckbox], requestCommits: false, LabelReads(HousingText.ConfirmLabel)));
    }

    [Fact]
    public void ANonCommittingPressNeedsItsOwnLabelAndNoCommittingOne()
    {
        Assert.True(ButtonLabelRule.MayPress(HousingTextRows.NoLabels.AsSpan(), requestCommits: false, LabelReads(HousingText.NoLabel)));
        Assert.False(ButtonLabelRule.MayPress(HousingTextRows.NoLabels.AsSpan(), requestCommits: false, LabelReads(HousingText.YesLabel)));
        Assert.False(ButtonLabelRule.MayPress(HousingTextRows.NoLabels.AsSpan(), requestCommits: false, LabelReads(HousingText.NoLabel, HousingText.ConfirmLabel)));
    }

    [Fact]
    public void ACommittingPressNeedsItsOwnLabelAndMayShareItWithAnotherCommittingRow()
    {
        Assert.True(ButtonLabelRule.MayPress(HousingTextRows.YesLabels.AsSpan(), requestCommits: true, LabelReads(HousingText.YesLabel, HousingText.ConfirmLabel)));
        Assert.False(ButtonLabelRule.MayPress(HousingTextRows.YesLabels.AsSpan(), requestCommits: true, LabelReads(HousingText.NoLabel)));
        Assert.False(ButtonLabelRule.MayPress([HousingText.ConfirmLabel], requestCommits: true, LabelReads(HousingText.EnterLotteryButton)));
    }

    [Fact]
    public void ResultDialogsAreAllAddonRows()
    {
        for (var textIndex = 0; textIndex < HousingTextRows.ResultDialogs.Length; textIndex++)
        {
            Assert.Equal(HousingSheet.Addon, HousingTextRows.Of(HousingTextRows.ResultDialogs[textIndex]).Sheet);
        }
    }

    private static Func<HousingText, bool> LabelReads(params HousingText[] labels) => label => Array.IndexOf(labels, label) >= 0;
}
