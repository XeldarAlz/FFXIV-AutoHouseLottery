using AutoHouseLottery.Core.Game.Model;
using Dalamud.Game;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using Lumina.Text.Payloads;
using Lumina.Text.ReadOnly;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoHouseLottery.Core.Game.Dialogs;

// The housing rows of the Addon and LogMessage sheets in the client's language, as patterns of their static text.
// Framework thread only; reloaded when the client language changes.
internal static class HousingSheetText
{
    private const string Space = " ";

    private static readonly SheetTextPattern[] patterns = new SheetTextPattern[HousingTextRows.All.Length];
    private static ClientLanguage? loadedLanguage;

    public static SheetTextPattern Pattern(HousingText text)
    {
        EnsureLoaded();
        return patterns[(int)text];
    }

    public static bool Matches(HousingText text, string shown) => Pattern(text).Matches(shown);

    public static HousingText? Match(string shown, ReadOnlySpan<HousingText> candidates)
    {
        for (var candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
        {
            if (Matches(candidates[candidateIndex], shown))
            {
                return candidates[candidateIndex];
            }
        }

        return null;
    }

    public static ClientLanguage Language
    {
        get
        {
            EnsureLoaded();
            return loadedLanguage!.Value;
        }
    }

    private static void EnsureLoaded()
    {
        var language = Svc.ClientState.ClientLanguage;
        if (loadedLanguage == language)
        {
            return;
        }

        for (var rowIndex = 0; rowIndex < HousingTextRows.All.Length; rowIndex++)
        {
            var row = HousingTextRows.All[rowIndex];
            patterns[rowIndex] = Load(row, language);
        }

        loadedLanguage = language;
    }

    private static SheetTextPattern Load(HousingTextRow row, ClientLanguage language)
    {
        ReadOnlySeString? text;
        try
        {
            text = row.Sheet == HousingSheet.Addon
                ? Svc.Data.GetExcelSheet<Addon>(language).GetRowOrDefault(row.RowId)?.Text
                : Svc.Data.GetExcelSheet<LogMessage>(language).GetRowOrDefault(row.RowId)?.Text;
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, $"Housing text: {row.Sheet} row {row.RowId} could not be read ({language})");
            return SheetTextPattern.Empty;
        }

        if (text is not { } found)
        {
            RunLog.Warning($"Housing text: {row.Sheet} row {row.RowId} ({row.Text}) is missing in the {language} sheet; that text is never matched.");
            return SheetTextPattern.Empty;
        }

        var pattern = ToPattern(found);
        if (!pattern.IsUsable)
        {
            RunLog.Warning($"Housing text: {row.Sheet} row {row.RowId} ({row.Text}) has no static text in {language}; it is never matched.");
        }
        else
        {
            RunLog.Debug($"Housing text: {row.Sheet} row {row.RowId} ({row.Text}) loaded in {language} as {pattern.Segments.Length} segment(s).");
        }

        return pattern;
    }

    // Line breaks and non-breaking spaces read as spaces; styling macros render nothing; every other macro is a gap.
    private static SheetTextPattern ToPattern(ReadOnlySeString text)
    {
        var tokens = new List<SheetTextToken>();
        foreach (var payload in text)
        {
            if (payload.Type == ReadOnlySePayloadType.Text)
            {
                tokens.Add(SheetTextToken.Literal(Encoding.UTF8.GetString(payload.Body.Span)));
                continue;
            }

            switch (payload.MacroCode)
            {
                case MacroCode.NewLine:
                case MacroCode.NonBreakingSpace:
                    tokens.Add(SheetTextToken.Literal(Space));
                    break;
                case MacroCode.SoftHyphen:
                case MacroCode.Color:
                case MacroCode.EdgeColor:
                case MacroCode.ShadowColor:
                case MacroCode.ColorType:
                case MacroCode.EdgeColorType:
                case MacroCode.Bold:
                case MacroCode.Italic:
                case MacroCode.Edge:
                case MacroCode.Shadow:
                case MacroCode.Scale:
                    break;
                default:
                    tokens.Add(SheetTextToken.Gap);
                    break;
            }
        }

        return SheetTextPattern.FromTokens(CollectionsMarshal.AsSpan(tokens));
    }
}
