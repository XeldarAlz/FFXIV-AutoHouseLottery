using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Eligibility.Model;

internal readonly record struct CycleParticipation(CycleKey Cycle, EntrySlotState State)
{
    public PlotKey? EnteredPlot { get; init; }

    public DateTime? EnteredUtc { get; init; }

    public DateTime? ObservedUtc { get; init; }

    public EntrySlotState StateIn(CycleKey cycle) => Cycle == cycle ? State : EntrySlotState.Unknown;
}
