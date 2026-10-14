using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Saved.Model;

internal readonly record struct NoticeRecord(NoticeKind Kind, PlotKey Plot, string SourceName, DateTime AtUtc, DateTime? PhaseEndUtc);
