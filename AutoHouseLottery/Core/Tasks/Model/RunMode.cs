namespace AutoHouseLottery.Core.Tasks.Model;

// Exact: one plot, never another. BestMatch: the frozen shortlist in its ranked order.
internal enum RunMode : byte { Exact, BestMatch }
