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
    private static readonly SheetTextPattern[] patterns = new SheetTextPattern[HousingTextRows.All.Length];
    private static ClientLanguage? loadedLanguage;

    public static ClientLanguage Language
    {
        get
        {
            EnsureLoaded();
            return loadedLanguage!.Value;
        }
    }

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

    // A row in any installed client language, uncached, for the probe's cross-language self-check.
    public static SheetTextPattern PatternIn(HousingText text, ClientLanguage language) => Load(HousingTextRows.Of(text), language);

    // The row rendered by the game's own evaluator with empty parameters and extracted like the readers extract a
    // dialog, so the self-check sees the same shape a window shows.
    public static string RenderedSample(HousingText text, ClientLanguage language)
    {
        var row = HousingTextRows.Of(text);
        var rendered = row.Sheet == HousingSheet.Addon
            ? Svc.SeStringEvaluator.EvaluateFromAddon(row.RowId, default, language)
            : Svc.SeStringEvaluator.EvaluateFromLogMessage(row.RowId, default, language);
        return rendered.ExtractText();
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
            var pattern = Load(row, language);
            patterns[rowIndex] = pattern;
            if (pattern.IsUsable)
            {
                RunLog.Debug($"Housing text: {row.Sheet} row {row.RowId} ({row.Text}) loaded in {language} as {pattern.Segments.Length} segment(s).");
            }
            else
            {
                RunLog.Warning($"Housing text: {row.Sheet} row {row.RowId} ({row.Text}) has no static text in {language}; it is never matched.");
            }
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

        return ToPattern(found);
    }

    private static SheetTextPattern ToPattern(ReadOnlySeString text)
    {
        var tokens = new List<SheetTextToken>();
        foreach (var payload in text)
        {
            tokens.Add(payload.Type == ReadOnlySePayloadType.Text
                ? SheetTextToken.Literal(Encoding.UTF8.GetString(payload.Body.Span))
                : SheetTextToken.ForMacro(KindOf(payload.MacroCode)));
        }

        return SheetTextPattern.FromTokens(CollectionsMarshal.AsSpan(tokens));
    }

    // Mirrors what Lumina's ExtractText makes of each macro, which is how every reader extracts the shown text.
    private static SheetMacroKind KindOf(MacroCode code) => code switch
    {
        MacroCode.NewLine => SheetMacroKind.LineBreak,
        MacroCode.NonBreakingSpace => SheetMacroKind.NonBreakingSpace,
        MacroCode.Hyphen => SheetMacroKind.Hyphen,
        MacroCode.SoftHyphen
            or MacroCode.Color
            or MacroCode.EdgeColor
            or MacroCode.ShadowColor
            or MacroCode.ColorType
            or MacroCode.EdgeColorType
            or MacroCode.Bold
            or MacroCode.Italic
            or MacroCode.Edge
            or MacroCode.Shadow
            or MacroCode.Scale => SheetMacroKind.Invisible,
        _ => SheetMacroKind.Dynamic,
    };
}
