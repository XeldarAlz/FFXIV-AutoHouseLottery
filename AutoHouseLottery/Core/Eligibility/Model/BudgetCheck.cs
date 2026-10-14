namespace AutoHouseLottery.Core.Eligibility.Model;

internal enum BudgetCheck : byte { Fits, DepositUnknown, OverDepositCap, BalanceUnknown, BelowReserve, Overflow }
