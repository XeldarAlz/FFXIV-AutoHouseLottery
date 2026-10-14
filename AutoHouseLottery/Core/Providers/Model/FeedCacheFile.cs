using System.Text.Json.Serialization;

namespace AutoHouseLottery.Core.Providers.Model;

internal sealed class FeedCacheFile
{
    public const int CurrentSchema = 1;

    [JsonPropertyName("schema")] public int? Schema { get; init; }

    [JsonPropertyName("worldId")] public uint? WorldId { get; init; }

    [JsonPropertyName("receivedUtc")] public DateTime? ReceivedUtc { get; init; }

    [JsonPropertyName("upstreamUtc")] public DateTime? UpstreamUtc { get; init; }

    [JsonPropertyName("body")] public string? Body { get; init; }
}
