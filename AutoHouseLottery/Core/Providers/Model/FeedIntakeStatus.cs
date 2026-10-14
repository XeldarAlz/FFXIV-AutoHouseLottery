namespace AutoHouseLottery.Core.Providers.Model;

internal enum FeedIntakeStatus : byte { Applied, FetchFailed, Stale, Malformed, WrongWorld }
