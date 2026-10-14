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
    public void FindsEachRejectionByItsLogMessageRow(uint rowId)
    {
        Assert.True(HousingTextRows.TryFindLogMessage(rowId, out var text));
        Assert.Equal(HousingSheet.LogMessage, HousingTextRows.Of(text).Sheet);
    }

    [Theory]
    [InlineData(7046u)]
    [InlineData(0u)]
    [InlineData(6098u)]
    public void AnAddonRowOrUnknownIdIsNoRejection(uint rowId)
    {
        Assert.False(HousingTextRows.TryFindLogMessage(rowId, out _));
    }

    [Fact]
    public void ResultDialogsAreAllAddonRows()
    {
        for (var textIndex = 0; textIndex < HousingTextRows.ResultDialogs.Length; textIndex++)
        {
            Assert.Equal(HousingSheet.Addon, HousingTextRows.Of(HousingTextRows.ResultDialogs[textIndex]).Sheet);
        }
    }
}
