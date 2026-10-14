namespace AutoHouseLottery.Core.Providers.Model;

internal readonly record struct RefreshTimer
{
    public DateTime LastAttemptUtc { get; init; }

    public DateTime LastSuccessUtc { get; init; }

    public int ConsecutiveFailures { get; init; }

    public DateTime BackoffUntilUtc { get; init; }

    public DateTime RetryAfterUtc { get; init; }

    public DateTime FreshUntilUtc { get; init; }
}
