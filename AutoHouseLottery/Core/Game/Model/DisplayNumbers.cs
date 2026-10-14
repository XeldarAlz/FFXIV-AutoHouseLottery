using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Game.Model;

// Numbers as the game renders them into a macro: digits with the client language's grouping marks between them.
internal static class DisplayNumbers
{
    private const ulong DecimalBase = 10;
    private const ulong Largest = long.MaxValue;
    private const int GroupLength = 3;
    private const char NoBreakSpace = ' ';
    private const char NarrowNoBreakSpace = ' ';

    // The first run of digits in the texts, read through grouping marks; null when there is none or it does not fit.
    public static ulong? First(ImmutableArray<string> texts)
    {
        for (var textIndex = 0; textIndex < texts.Length; textIndex++)
        {
            var text = texts[textIndex];
            ulong value = 0;
            var digits = 0;
            for (var characterIndex = 0; characterIndex < text.Length; characterIndex++)
            {
                var character = text[characterIndex];
                if (char.IsAsciiDigit(character))
                {
                    value = (value * DecimalBase) + (ulong)(character - '0');
                    digits++;
                    if (value > Largest)
                    {
                        return null;
                    }

                    continue;
                }

                if (digits > 0 && !IsGroupingMark(character))
                {
                    break;
                }
            }

            if (digits > 0)
            {
                return value;
            }
        }

        return null;
    }

    // A text that is one number and nothing else: digits, optionally in groups of three after the first, all split by
    // the same grouping mark. Anything else (a stray character, a mixed or misplaced mark, a leading zero, an overflow)
    // reads as no number, so an amount the plugin is not sure of is never acted on.
    public static long? Whole(string text)
    {
        var shown = text.AsSpan().Trim();
        if (shown.IsEmpty || (shown[0] == '0' && shown.Length > 1))
        {
            return null;
        }

        ulong value = 0;
        var digitsInGroup = 0;
        var groups = 0;
        var mark = '\0';
        for (var characterIndex = 0; characterIndex < shown.Length; characterIndex++)
        {
            var character = shown[characterIndex];
            if (char.IsAsciiDigit(character))
            {
                value = (value * DecimalBase) + (ulong)(character - '0');
                digitsInGroup++;
                if (value > Largest)
                {
                    return null;
                }

                continue;
            }

            var groupFits = groups == 0 ? digitsInGroup is > 0 and <= GroupLength : digitsInGroup == GroupLength;
            if (!IsGroupingMark(character) || !groupFits || (groups > 0 && character != mark))
            {
                return null;
            }

            mark = character;
            groups++;
            digitsInGroup = 0;
        }

        var lastGroupFits = groups == 0 ? digitsInGroup > 0 : digitsInGroup == GroupLength;
        return lastGroupFits ? (long)value : null;
    }

    private static bool IsGroupingMark(char character) => character is ',' or '.' or ' ' or NoBreakSpace or NarrowNoBreakSpace;
}
