namespace AutoHouseLottery.Core.Find.Model;

[Flags]
internal enum StatusFilter : byte
{
    None = 0,
    EntryOpen = 1 << (int)PlotStatus.EntryOpen,
    Results = 1 << (int)PlotStatus.Results,
    NextCycle = 1 << (int)PlotStatus.NextCycle,
    ForSale = 1 << (int)PlotStatus.ForSale,
    Occupied = 1 << (int)PlotStatus.Occupied,
    NotReported = 1 << (int)PlotStatus.NotReported,
    NeedsRefresh = 1 << (int)PlotStatus.NeedsRefresh,
}
