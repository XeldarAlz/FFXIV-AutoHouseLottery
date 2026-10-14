using System.Text.Json.Serialization;

namespace AutoHouseLottery.Core.Providers.Model;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(FeedWorldSummary[]))]
[JsonSerializable(typeof(FeedWorldDetail))]
[JsonSerializable(typeof(FeedCacheFile))]
internal sealed partial class FeedJsonContext : JsonSerializerContext
{
}
