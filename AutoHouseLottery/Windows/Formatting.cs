using AutoHouseLottery.Core;
using AutoHouseLottery.Core.Localization;

namespace AutoHouseLottery.Windows;

// Shared display formatters so every page renders gil, ages and durations the same way.
internal static class Formatting
{
    private const int HoursPerDay = 24;
    private const int DaysShownAsAge = 7;
    private const string MonthDayFormat = "M";
    private const string NoTime = "--:--";

    public static string Elapsed(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes:D2}m";
        }

        return $"{duration.Minutes}m {duration.Seconds:D2}s";
    }

    public static string Countdown(TimeSpan remaining)
        => remaining.TotalHours >= 1 ? $"{(int)remaining.TotalHours}h {remaining.Minutes:D2}m" : $"{remaining.Minutes}m";

    public static string Gil(long amount) => Loc.Gil(amount);

    public static string RelativeTime(DateTime utc) => RelativeTime(utc, DateTime.UtcNow);

    public static string RelativeTime(DateTime utc, DateTime nowUtc)
    {
        var span = nowUtc - utc;
        if (span.TotalSeconds < TimeUnits.SecondsPerMinute)
        {
            return Loc.T(L.Time.JustNow);
        }

        if (span.TotalMinutes < TimeUnits.MinutesPerHour)
        {
            return Loc.T(L.Time.MinutesAgo, (int)span.TotalMinutes);
        }

        if (span.TotalHours < HoursPerDay)
        {
            return Loc.T(L.Time.HoursAgo, (int)span.TotalHours);
        }

        return span.TotalDays < DaysShownAsAge
            ? Loc.T(L.Time.DaysAgo, (int)span.TotalDays)
            : utc.ToLocalTime().ToString(MonthDayFormat, Loc.Culture);
    }

    public static string Time(float seconds)
    {
        if (seconds <= 0)
        {
            return NoTime;
        }

        var wholeSeconds = (int)seconds;
        return $"{wholeSeconds / TimeUnits.SecondsPerMinute}:{wholeSeconds % TimeUnits.SecondsPerMinute:D2}";
    }
}
