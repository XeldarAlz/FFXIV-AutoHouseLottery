using System.Text.Json.Serialization;

namespace AutoHouseLottery.Core.Providers.Model;

internal sealed class FeedDistrictDetail
{
    [JsonPropertyName("id")] public uint? Id { get; init; }

    [JsonPropertyName("name")] public string? Name { get; init; }

    [JsonPropertyName("num_open_plots")] public int? OpenPlotCount { get; init; }

    [JsonPropertyName("open_plots")] public FeedOpenPlot[]? OpenPlots { get; init; }
}
