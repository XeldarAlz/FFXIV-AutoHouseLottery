namespace AutoHouseLottery.Core.Persistence.Model;

internal enum AttemptState : byte { Prepared, Dispatched, Confirmed, Rejected, NeedsReconciliation }
