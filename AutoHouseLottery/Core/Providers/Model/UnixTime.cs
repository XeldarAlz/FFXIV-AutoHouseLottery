namespace AutoHouseLottery.Core.Providers.Model;

internal static class UnixTime
{
    private static readonly double latestSeconds = (DateTime.MaxValue - DateTime.UnixEpoch).TotalSeconds;

    public static bool TryFromSeconds(double? seconds, out DateTime utc)
    {
        utc = default;
        if (seconds is not { } value || !double.IsFinite(value) || value <= 0d || value >= latestSeconds)
        {
            return false;
        }

        utc = DateTime.UnixEpoch.AddTicks((long)(value * TimeSpan.TicksPerSecond));
        return true;
    }

    public static bool TryFromSeconds(long? seconds, out DateTime utc)
    {
        utc = default;
        return seconds is { } value && TryFromSeconds((double)value, out utc);
    }
}
