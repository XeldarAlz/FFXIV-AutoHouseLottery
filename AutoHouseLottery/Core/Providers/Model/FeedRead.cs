namespace AutoHouseLottery.Core.Providers.Model;

internal readonly record struct FeedRead(FeedIntakeStatus Status, WorldFeed? Feed);
