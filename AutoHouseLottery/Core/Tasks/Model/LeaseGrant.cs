using System.Threading;

namespace AutoHouseLottery.Core.Tasks.Model;

internal readonly record struct LeaseGrant(LeaseOwner Owner, CancellationToken Cancellation);
