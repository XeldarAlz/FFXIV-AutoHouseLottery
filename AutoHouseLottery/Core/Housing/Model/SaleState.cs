namespace AutoHouseLottery.Core.Housing.Model;

internal readonly record struct SaleState(PlotSale Sale, DateTime? PhaseEndUtc);
