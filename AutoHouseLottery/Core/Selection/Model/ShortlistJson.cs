using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Selection.Model;

internal readonly struct ShortlistJson
{
    public int? Schema { get; init; }

    public Guid? Id { get; init; }

    public string? Name { get; init; }

    public PlotKeyJson[]? Plots { get; init; }
}
