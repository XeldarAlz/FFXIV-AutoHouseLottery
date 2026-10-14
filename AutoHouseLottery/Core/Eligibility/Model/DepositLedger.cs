using AutoHouseLottery.Core.Shared;

namespace AutoHouseLottery.Core.Eligibility.Model;

internal readonly record struct DepositLedger(long GrossSubmitted, long PendingReservations, long RefundsReceived)
{
    public bool TryReserve(long amount, out DepositLedger ledger)
    {
        ledger = this;
        if (amount <= 0 || !Gil.TryAdd(PendingReservations, amount, out var pending))
        {
            return false;
        }

        ledger = this with { PendingReservations = pending };
        return true;
    }

    public bool TryConfirm(long amount, out DepositLedger ledger)
    {
        ledger = this;
        if (amount <= 0 || amount > PendingReservations || !Gil.TryAdd(GrossSubmitted, amount, out var gross))
        {
            return false;
        }

        ledger = this with { GrossSubmitted = gross, PendingReservations = PendingReservations - amount };
        return true;
    }

    public bool TryRelease(long amount, out DepositLedger ledger)
    {
        ledger = this;
        if (amount <= 0 || amount > PendingReservations)
        {
            return false;
        }

        ledger = this with { PendingReservations = PendingReservations - amount };
        return true;
    }

    public bool TryRecordRefund(long amount, out DepositLedger ledger)
    {
        ledger = this;
        if (amount <= 0 || !Gil.TryAdd(RefundsReceived, amount, out var refunds))
        {
            return false;
        }

        ledger = this with { RefundsReceived = refunds };
        return true;
    }
}
