namespace AutoHouseLottery.Core.Providers.Model;

internal enum FeedFailure : byte { None, Unreachable, TimedOut, RateLimited, HttpError, Oversized, Malformed }
