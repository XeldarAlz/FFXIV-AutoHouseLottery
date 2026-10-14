namespace AutoHouseLottery.Core.Tasks.Model;

// Exact: one plot, never another. BestMatch: the frozen shortlist in its ranked order.
internal enum RunMode : byte { Exact, BestMatch }

// What the run does at the confirmation. DryRun reads it and closes it; Real confirms.
internal enum EntryMode : byte { DryRun, Real }
