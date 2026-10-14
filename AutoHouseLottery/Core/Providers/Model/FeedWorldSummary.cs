using System.Text.Json.Serialization;

namespace AutoHouseLottery.Core.Providers.Model;

internal sealed class FeedWorldSummary
{
    [JsonPropertyName("id")] public uint? Id { get; init; }

    [JsonPropertyName("name")] public string? Name { get; init; }

    [JsonPropertyName("datacenter_id")] public int? DataCenterId { get; init; }

    [JsonPropertyName("datacenter_name")] public string? DataCenterName { get; init; }
}
