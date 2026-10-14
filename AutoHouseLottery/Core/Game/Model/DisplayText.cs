using System.Text;

namespace AutoHouseLottery.Core.Game.Model;

// The game wraps, hyphenates and decorates what it shows, so displayed text and sheet text are compared in this form.
internal static class DisplayText
{
    private const char SoftHyphen = '­';
    private const char ZeroWidthSpace = '​';
    private const char FirstGameGlyph = '';
    private const char LastGameGlyph = '';
    private const char Space = ' ';

    public static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        for (var characterIndex = 0; characterIndex < text.Length; characterIndex++)
        {
            var character = text[characterIndex];
            if (character is SoftHyphen or ZeroWidthSpace or (>= FirstGameGlyph and <= LastGameGlyph))
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(Space);
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
