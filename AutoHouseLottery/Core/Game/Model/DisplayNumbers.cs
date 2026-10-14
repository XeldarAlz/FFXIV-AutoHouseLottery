using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Game.Model;

// Numbers as the game renders them into a macro: digits with the client language's grouping marks between them.
internal static class DisplayNumbers
{
    private const ulong DecimalBase = 10;
    private const ulong Largest = long.MaxValue;
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

    private static bool IsGroupingMark(char character) => character is ',' or '.' or ' ' or NoBreakSpace or NarrowNoBreakSpace;
}
