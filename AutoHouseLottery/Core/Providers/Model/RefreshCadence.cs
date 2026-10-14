namespace AutoHouseLottery.Core.Providers.Model;

// The conservative starting cadence Aetherphone settled on: one world every 20 minutes while the window is open, every
// hour while only saved searches need it, never more often than the manual cooldown, and an exponential, jittered
// backoff after failures. A Retry-After from the service is a hard floor that a manual refresh cannot skip.
internal static class RefreshCadence
{
    public const int ActiveMinutes = 20;
    public const int IdleMinutes = 60;
    public const int ManualCooldownSeconds = 15;
    public const int FirstBackoffMinutes = 1;
    public const int MaximumBackoffMinutes = 30;
    public const int MaximumRetryAfterHours = 6;

    private const int MaximumDoublings = 10;
    private const double JitterFloor = 0.75;
    private const double JitterSpread = 0.5;

    public static readonly TimeSpan ActiveInterval = TimeSpan.FromMinutes(ActiveMinutes);
    public static readonly TimeSpan IdleInterval = TimeSpan.FromMinutes(IdleMinutes);
    public static readonly TimeSpan ManualCooldown = TimeSpan.FromSeconds(ManualCooldownSeconds);
    public static readonly TimeSpan MaximumBackoff = TimeSpan.FromMinutes(MaximumBackoffMinutes);
    public static readonly TimeSpan MaximumRetryAfter = TimeSpan.FromHours(MaximumRetryAfterHours);

    private static readonly TimeSpan firstBackoff = TimeSpan.FromMinutes(FirstBackoffMinutes);

    public static bool IsDue(in RefreshTimer timer, RefreshMode mode, DateTime nowUtc)
    {
        if (mode == RefreshMode.Paused || nowUtc < timer.RetryAfterUtc || nowUtc < timer.BackoffUntilUtc)
        {
            return false;
        }

        if (timer.LastAttemptUtc == default || timer.ConsecutiveFailures > 0)
        {
            return true;
        }

        return nowUtc - timer.LastAttemptUtc >= (mode == RefreshMode.Active ? ActiveInterval : IdleInterval);
    }

    public static TimeSpan ManualWait(in RefreshTimer timer, DateTime nowUtc)
    {
        var allowedAt = ManualAllowedAt(timer);
        return allowedAt > nowUtc ? allowedAt - nowUtc : TimeSpan.Zero;
    }

    public static DateTime ManualAllowedAt(in RefreshTimer timer)
    {
        var allowedAt = timer.LastAttemptUtc == default ? default : timer.LastAttemptUtc + ManualCooldown;
        if (timer.RetryAfterUtc > allowedAt)
        {
            allowedAt = timer.RetryAfterUtc;
        }

        return timer.FreshUntilUtc > allowedAt ? timer.FreshUntilUtc : allowedAt;
    }

    public static RefreshTimer Started(in RefreshTimer timer, DateTime nowUtc) => timer with { LastAttemptUtc = nowUtc };

    public static RefreshTimer Succeeded(in RefreshTimer timer, DateTime nowUtc, TimeSpan? maxAge)
    {
        var fresh = maxAge is { } age && age > TimeSpan.Zero ? nowUtc + (age < ActiveInterval ? age : ActiveInterval) : default;
        return timer with
        {
            ConsecutiveFailures = 0,
            BackoffUntilUtc = default,
            RetryAfterUtc = default,
            FreshUntilUtc = fresh,
        };
    }

    public static RefreshTimer Failed(in RefreshTimer timer, DateTime nowUtc, TimeSpan? retryAfter, double jitter)
    {
        var failures = timer.ConsecutiveFailures + 1;
        var directed = retryAfter is { } wait && wait > TimeSpan.Zero ? nowUtc + (wait < MaximumRetryAfter ? wait : MaximumRetryAfter) : default;
        return timer with
        {
            ConsecutiveFailures = failures,
            BackoffUntilUtc = nowUtc + Backoff(failures, jitter),
            RetryAfterUtc = directed,
            FreshUntilUtc = default,
        };
    }

    public static TimeSpan Backoff(int failures, double jitter)
    {
        var doublings = Math.Clamp(failures - 1, 0, MaximumDoublings);
        var exponential = firstBackoff * (1L << doublings);
        var capped = exponential < MaximumBackoff ? exponential : MaximumBackoff;
        return capped * (JitterFloor + JitterSpread * Math.Clamp(jitter, 0d, 1d));
    }
}
