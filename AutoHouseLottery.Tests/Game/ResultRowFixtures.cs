using AutoHouseLottery.Core.Game.Model;
using System.Text;

namespace AutoHouseLottery.Tests.Game;

// Addon rows 7122, 7123, 7124 and 7128 in the four client languages, as the ffxiv-datamining sheets show them, with
// each macro written as {gap}, each line-break macro as \n and each soft-hyphen macro as ­. The plugin itself
// reads these rows from the client; the fixtures only let the matcher be tested against the real row structure.
internal static class ResultRowFixtures
{
    public const string GapMarker = "{gap}";
    private const char LineBreakMarker = '\n';
    private const char SoftHyphenMarker = '­';
    // Lumina's ExtractText writes a line-break macro as Environment.NewLine, which is this on the game's platform.
    private const string ExtractedLineBreak = "\r\n";

    public static readonly string[] Languages = ["en", "de", "fr", "ja"];

    public static readonly int[] Rows = [7122, 7123, 7124, 7128];

    private static readonly Dictionary<(string Language, int Row), string> templates = new()
    {
        [("en", 7122)] = "The winning number is...{gap}!\n\nCongratulations! You are the winner of this lottery. Finalize your purchase and claim your plot of land?\n※Plot available until {gap}.",
        [("en", 7123)] = "The results period has ended. Your claim to this plot of land has been forfeit. Additionally, a cancellation fee of 50% will be deducted from your deposit. Accept the remaining refund of {gap} gil?\n※Refund available until {gap}.",
        [("en", 7124)] = "The winning number is...{gap}. May you have better luck in the future!\n\nAccept a full refund of your deposit of {gap} gil?\n※Refund available until {gap}.",
        [("en", 7128)] = "Congratulations! As the sole entrant of this lottery, you have won by default. Finalize your purchase and claim your plot of land?\n※Plot available until {gap}.",
        [("de", 7122)] = "Die Gewinnzahl ist {gap}. Herzlichen Glückwunsch, du hast gewonnen! Möchtest du den Kauf des Grundstücks abschließen?\n\n※ Das Angebot ist bis {gap} gültig.",
        [("de", 7123)] = "Die Frist zum Kauf des Grundstücks ist abgelaufen und es fällt eine Stornogebühr in Höhe von 50 % an. Willst du die rückerstatteten {gap} Gil erhalten?\n\n※ Die Rückerstattung ist bis {gap} möglich.",
        [("de", 7124)] = "Die Gewinnzahl ist {gap}. Diesmal war dir das Glück leider nicht hold. Möchtest du die Rückerstattung über {gap} Gil erhalten?\n\n※ Die Rückerstattung ist bis {gap} möglich.",
        [("de", 7128)] = "Herzlichen Glückwunsch! Da es keine weiteren Teilnehmer an der Auslosung gab, hast du automatisch gewonnen! Möchtest du den Kauf des Grundstücks abschließen?\n\n※ Das Angebot ist bis {gap} gültig.",
        [("fr", 7122)] = "Le numéro gagnant est le... {gap} ! Fé­li­ci­ta­tions, vous avez gagné le droit d'acquérir ce terrain ! Voulez-vous confirmer votre achat ?\n\nPériode des résultats : jusqu'au {gap}",
        [("fr", 7123)] = "L'achat n'ayant pas été validé durant la période des résultats, vous ne pouvez récupérer que la moitié des frais de par­ti­ci­pa­tion, à savoir {gap} gil. Voulez-vous confirmer leur réception ?\n\nPériode de remboursement : jusqu'au {gap}",
        [("fr", 7124)] = "Le numéro gagnant est le... {gap}. Dommage, mais la prochaine fois sera peut-être la bonne ! Voulez-vous confirmer le remboursement de {gap} gil ?\n\nPériode de remboursement : jusqu'au {gap}",
        [("fr", 7128)] = "Fé­li­ci­ta­tions, comme vous étiez le seul participant, vous avez gagné le droit d'acquérir ce terrain ! Voulez-vous confirmer votre achat ?\n\nPériode des résultats : jusqu'au {gap}",
        [("ja", 7122)] = "当選番号は{gap}です。\nおめでとうございます！当選しました！\nこの土地を取得してよろしいですか？\n\n※取得期限：{gap}まで",
        [("ja", 7123)] = "土地の取得期限を過ぎているため、\nキャンセル料として50％が減額され、\n{gap}ギルが返還されます。\n受け取りますか？\n\n※返還期限：{gap}まで",
        [("ja", 7124)] = "当選番号は{gap}です。\n落選しました。\n{gap}ギルが返還されます。\n受け取りますか？\n\n※返還期限：{gap}まで",
        [("ja", 7128)] = "おめでとうございます！\n抽選参加人数が1人だったため、購入権を獲得しました！\nこの土地を取得してよろしいですか？\n\n※取得期限：{gap}",
    };

    private static readonly string[] gapValues = ["1234", "3,000,000", "2026/10/24 8:00"];

    // The pattern the plugin builds from the row: the same tokens HousingSheetText makes of the sheet's payloads.
    public static SheetTextPattern Pattern(string language, int row)
    {
        var template = templates[(language, row)];
        var tokens = new List<SheetTextToken>();
        var literal = new StringBuilder();
        for (var characterIndex = 0; characterIndex < template.Length; characterIndex++)
        {
            if (template.AsSpan(characterIndex).StartsWith(GapMarker))
            {
                FlushLiteral(literal, tokens);
                tokens.Add(SheetTextToken.Gap);
                characterIndex += GapMarker.Length - 1;
                continue;
            }

            var character = template[characterIndex];
            if (character is LineBreakMarker or SoftHyphenMarker)
            {
                FlushLiteral(literal, tokens);
                tokens.Add(SheetTextToken.ForMacro(character == LineBreakMarker ? SheetMacroKind.LineBreak : SheetMacroKind.Invisible));
                continue;
            }

            literal.Append(character);
        }

        FlushLiteral(literal, tokens);
        return SheetTextPattern.FromTokens(tokens.ToArray());
    }

    // The row as a dialog shows it once extracted: macros filled in, line breaks as new lines, soft hyphens gone.
    public static string Shown(string language, int row)
    {
        var template = templates[(language, row)];
        var shown = new StringBuilder();
        var gapIndex = 0;
        for (var characterIndex = 0; characterIndex < template.Length; characterIndex++)
        {
            if (template.AsSpan(characterIndex).StartsWith(GapMarker))
            {
                shown.Append(gapValues[gapIndex++ % gapValues.Length]);
                characterIndex += GapMarker.Length - 1;
                continue;
            }

            var character = template[characterIndex];
            if (character == LineBreakMarker)
            {
                shown.Append(ExtractedLineBreak);
            }
            else if (character != SoftHyphenMarker)
            {
                shown.Append(character);
            }
        }

        return shown.ToString();
    }

    private static void FlushLiteral(StringBuilder literal, List<SheetTextToken> tokens)
    {
        if (literal.Length == 0)
        {
            return;
        }

        tokens.Add(SheetTextToken.Literal(literal.ToString()));
        literal.Clear();
    }
}
