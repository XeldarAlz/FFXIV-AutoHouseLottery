using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Game.Model;

// Numbers as the game renders them into a macro: ASCII digits, with the client language's grouping marks between groups
// of exactly three. Anything that is not exactly one such number reads as unknown, never as a smaller number.
internal static class DisplayNumbers
{
    private const long DecimalBase = 10;
    private const int GroupLength = 3;
    private const char NoBreakSpace = ' ';
    private const char NarrowNoBreakSpace = ' ';
    private const char ThinSpace = ' ';
    private const char RightSingleQuote = '’';
    private const char FullwidthComma = '，';
    private const char FullwidthFullStop = '．';

    // Characters that scale the number before them in Chinese and Japanese, so the digits alone are not the amount.
    private const string MagnitudeUnits = "十百千万萬億亿兆";

    // The one number among the texts. Null when no text holds a digit, more than one does, or that text holds anything
    // but one well-formed number: another run of digits, a malformed group, a digit that is not ASCII, a magnitude unit
    // after it, or a value beyond the 64-bit range.
    public static long? Single(ImmutableArray<string> texts)
    {
        long? found = null;
        for (var textIndex = 0; textIndex < texts.Length; textIndex++)
        {
            var text = texts[textIndex];
            if (FirstDigit(text) < 0)
            {
                continue;
            }

            if (found is not null || Parse(text) is not { } value)
            {
                return null;
            }

            found = value;
        }

        return found;
    }

    private static long? Parse(string text)
    {
        var start = FirstDigit(text);
        if (!char.IsAsciiDigit(text[start]))
        {
            return null;
        }

        var index = start;
        long value = 0;
        while (index < text.Length && char.IsAsciiDigit(text[index]))
        {
            if (!TryAppend(ref value, text[index]))
            {
                return null;
            }

            index++;
        }

        var leadingDigits = index - start;
        while (index < text.Length && IsGroupingMark(text[index]) && IsGroupAt(text, index + 1))
        {
            if (leadingDigits > GroupLength)
            {
                return null;
            }

            for (var groupIndex = index + 1; groupIndex <= index + GroupLength; groupIndex++)
            {
                if (!TryAppend(ref value, text[groupIndex]))
                {
                    return null;
                }
            }

            index += GroupLength + 1;
        }

        if (index < text.Length && MagnitudeUnits.Contains(text[index]))
        {
            return null;
        }

        return FirstDigit(text, index) < 0 ? value : null;
    }

    // Exactly three ASCII digits, not followed by a fourth.
    private static bool IsGroupAt(string text, int index)
    {
        if (index + GroupLength > text.Length)
        {
            return false;
        }

        for (var offset = 0; offset < GroupLength; offset++)
        {
            if (!char.IsAsciiDigit(text[index + offset]))
            {
                return false;
            }
        }

        return index + GroupLength == text.Length || !char.IsAsciiDigit(text[index + GroupLength]);
    }

    private static bool TryAppend(ref long value, char digit)
    {
        var added = digit - '0';
        if (value > (long.MaxValue - added) / DecimalBase)
        {
            return false;
        }

        value = (value * DecimalBase) + added;
        return true;
    }

    // Any digit counts here, ASCII or not, so a number the client writes in other digits is never half read.
    private static int FirstDigit(string text, int from = 0)
    {
        for (var index = from; index < text.Length; index++)
        {
            if (char.IsDigit(text[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsGroupingMark(char character)
        => character is ',' or '.' or ' ' or '\'' or NoBreakSpace or NarrowNoBreakSpace or ThinSpace or RightSingleQuote or FullwidthComma or FullwidthFullStop;
}
