using System.Globalization;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Saved.Model;

// A new match belongs to the search that found it; the other notices belong to the plot, so a plot kept in two lists is
// announced once. Every key carries the cycle, so the same plot can be announced again in a later lottery.
internal readonly record struct Notice(NoticeKind Kind, PlotKey Plot, Guid SourceId, string SourceName, CycleKey Cycle, DateTime? PhaseEndUtc)
{
    public string DedupeKey => Kind == NoticeKind.NewMatch
        ? string.Create(CultureInfo.InvariantCulture, $"{Kind}:{SourceId:N}:{Plot}:{Cycle}")
        : string.Create(CultureInfo.InvariantCulture, $"{Kind}:{Plot}:{Cycle}");
}
