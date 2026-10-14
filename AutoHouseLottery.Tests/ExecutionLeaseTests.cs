using AutoHouseLottery.Core.Tasks.Model;

namespace AutoHouseLottery.Tests;

public sealed class ExecutionLeaseTests
{
    [Fact]
    public void OnlyOneGameDrivingTaskHoldsTheLease()
    {
        var lease = new ExecutionLease();

        Assert.True(lease.TryAcquire(LeaseWork.Entry, out var entry));
        Assert.False(lease.TryAcquire(LeaseWork.Refund, out _));
        Assert.False(lease.TryAcquire(LeaseWork.CharacterSwitch, out _));
        Assert.Equal(entry.Owner, lease.Owner);
    }

    [Fact]
    public void ReleaseHandsTheLeaseToTheNextTask()
    {
        var lease = new ExecutionLease();
        Assert.True(lease.TryAcquire(LeaseWork.Scan, out var scan));

        Assert.True(lease.Release(scan.Owner));
        Assert.Null(lease.Owner);
        Assert.True(lease.TryAcquire(LeaseWork.Claim, out var claim));
        Assert.Equal(LeaseWork.Claim, claim.Owner.Work);
    }

    [Fact]
    public void StaleOwnerCannotReleaseALaterLease()
    {
        var lease = new ExecutionLease();
        Assert.True(lease.TryAcquire(LeaseWork.Entry, out var first));
        lease.Release(first.Owner);
        Assert.True(lease.TryAcquire(LeaseWork.Entry, out var second));

        Assert.False(lease.Release(first.Owner));
        Assert.True(lease.IsHeldBy(second.Owner));
    }

    [Fact]
    public void CancelSignalsOnlyTheCurrentOwner()
    {
        var lease = new ExecutionLease();
        Assert.False(lease.CancelOwner());
        Assert.True(lease.TryAcquire(LeaseWork.Refund, out var refund));

        Assert.True(lease.CancelOwner());

        Assert.True(refund.Cancellation.IsCancellationRequested);
        Assert.True(lease.IsHeldBy(refund.Owner));
    }
}
