using System.Threading;

namespace AutoHouseLottery.Core.Tasks.Model;

internal sealed class ExecutionLease
{
    private readonly Lock gate = new();
    private CancellationTokenSource? cancellation;
    private LeaseOwner? owner;
    private long lastTicket;

    public LeaseOwner? Owner
    {
        get
        {
            lock (gate)
            {
                return owner;
            }
        }
    }

    public bool TryAcquire(LeaseWork work, out LeaseGrant grant)
    {
        lock (gate)
        {
            grant = default;
            if (owner is not null)
            {
                return false;
            }

            lastTicket++;
            var acquired = new LeaseOwner(work, lastTicket);
            cancellation = new CancellationTokenSource();
            owner = acquired;
            grant = new LeaseGrant(acquired, cancellation.Token);
            return true;
        }
    }

    public bool IsHeldBy(LeaseOwner candidate)
    {
        lock (gate)
        {
            return owner == candidate;
        }
    }

    public bool Release(LeaseOwner holder)
    {
        lock (gate)
        {
            if (owner != holder)
            {
                return false;
            }

            owner = null;
            cancellation?.Dispose();
            cancellation = null;
            return true;
        }
    }

    public bool CancelOwner()
    {
        lock (gate)
        {
            if (cancellation is null)
            {
                return false;
            }

            cancellation.Cancel();
            return true;
        }
    }
}
