using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Saved.Model;

internal sealed class SavedListJson
{
    public Guid? Id { get; init; }

    public string? Name { get; init; }

    public DateTime? CreatedUtc { get; init; }

    public PlotKeyJson[]? Plots { get; init; }
}
