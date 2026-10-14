namespace AutoHouseLottery.Core.Providers.Model;

internal enum FetchStatus : byte { Success, HttpError, RateLimited, Oversized, TimedOut, Unreachable, Cancelled }
