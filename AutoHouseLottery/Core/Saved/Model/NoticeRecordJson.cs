using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Saved.Model;

internal sealed class NoticeRecordJson
{
    public NoticeKind? Kind { get; init; }

    public PlotKeyJson? Plot { get; init; }

    public string? Source { get; init; }

    public DateTime? AtUtc { get; init; }

    public DateTime? PhaseEndUtc { get; init; }
}
