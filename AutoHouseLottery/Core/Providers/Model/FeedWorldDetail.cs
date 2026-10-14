using System.Text.Json.Serialization;

namespace AutoHouseLottery.Core.Providers.Model;

internal sealed class FeedWorldDetail
{
    [JsonPropertyName("id")] public uint? Id { get; init; }

    [JsonPropertyName("name")] public string? Name { get; init; }

    [JsonPropertyName("num_open_plots")] public int? OpenPlotCount { get; init; }

    [JsonPropertyName("districts")] public FeedDistrictDetail[]? Districts { get; init; }
}
