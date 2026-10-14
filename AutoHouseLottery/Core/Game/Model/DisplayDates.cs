namespace AutoHouseLottery.Core.Game.Model;

// The order the client language writes a date's day, month and year in.
internal enum DateOrder : byte { YearMonthDay, MonthDayYear, DayMonthYear }

internal static class DisplayDates
{
    private const int PartCount = 5;
    private const int YearDigits = 4;
    private const int DecimalBase = 10;
    private const int LargestPart = 10_000;
    private const int HoursPerDay = 24;
    private const int MinutesPerHour = 60;
    private const int MonthsPerYear = 12;

    // A date and time the game rendered into a macro: exactly five runs of ASCII digits, the date in the client
    // language's order with a four-digit year, then the hour and the minute, split only by punctuation and spaces. Any
    // letter (a month name, a.m. or p.m.), another count of numbers or an impossible date reads as unknown, so a deadline
    // the plugin is not sure of falls back to its estimate. The result is the wall-clock time the game showed.
    public static DateTime? Parse(string text, DateOrder order)
    {
        Span<int> parts = stackalloc int[PartCount];
        Span<int> digits = stackalloc int[PartCount];
        var count = 0;
        var inNumber = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (char.IsAsciiDigit(character))
            {
                if (!inNumber)
                {
                    if (count == PartCount)
                    {
                        return null;
                    }

                    parts[count] = 0;
                    digits[count] = 0;
                    count++;
                    inNumber = true;
                }

                parts[count - 1] = (parts[count - 1] * DecimalBase) + (character - '0');
                digits[count - 1]++;
                if (parts[count - 1] >= LargestPart)
                {
                    return null;
                }

                continue;
            }

            if (char.IsLetter(character) || char.IsDigit(character))
            {
                return null;
            }

            inNumber = false;
        }

        if (count != PartCount)
        {
            return null;
        }

        var (yearIndex, monthIndex, dayIndex) = order switch
        {
            DateOrder.YearMonthDay => (0, 1, 2),
            DateOrder.MonthDayYear => (2, 0, 1),
            _ => (2, 1, 0),
        };
        var year = parts[yearIndex];
        var month = parts[monthIndex];
        var day = parts[dayIndex];
        var hour = parts[3];
        var minute = parts[4];
        if (digits[yearIndex] != YearDigits
            || year < 1
            || month is < 1 or > MonthsPerYear
            || day < 1 || day > DateTime.DaysInMonth(year, month)
            || hour >= HoursPerDay
            || minute >= MinutesPerHour)
        {
            return null;
        }

        return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
    }
}
