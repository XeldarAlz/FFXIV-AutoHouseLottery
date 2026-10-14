using System.Text.Json.Serialization;

namespace AutoHouseLottery.Core.Providers.Model;

internal sealed class FeedOpenPlot
{
    [JsonPropertyName("world_id")] public uint? WorldId { get; init; }

    [JsonPropertyName("district_id")] public uint? DistrictId { get; init; }

    [JsonPropertyName("ward_number")] public int? WardNumber { get; init; }

    [JsonPropertyName("plot_number")] public int? PlotNumber { get; init; }

    [JsonPropertyName("size")] public int? Size { get; init; }

    [JsonPropertyName("price")] public long? Price { get; init; }

    [JsonPropertyName("last_updated_time")] public double? LastUpdatedTime { get; init; }

    [JsonPropertyName("purchase_system")] public int? PurchaseSystem { get; init; }

    [JsonPropertyName("lotto_entries")] public int? LotteryEntries { get; init; }

    [JsonPropertyName("lotto_phase")] public int? LotteryPhase { get; init; }

    [JsonPropertyName("lotto_phase_until")] public long? LotteryPhaseUntil { get; init; }
}
