using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Selection.Model;
using AutoHouseLottery.Core.Tasks.Model;

namespace AutoHouseLottery.Core.Batch.Model;

// Queued: waits for its turn. Running: its entry run is going. Entered: a confirmed real entry. DryRun: its dry run reached
// the lottery confirmation and closed it. NotEntered: its run ended without an entry, for a reason about the plot or the
// character. Excluded: the plan left it out before the batch started. Uncertain: a press may have reached the game.
internal enum BatchMemberState : byte { Queued, Running, Entered, DryRun, NotEntered, Excluded, Uncertain }

// One selected character of a batch. Assignment is null for a character the plan excluded; Reasons then says why. Plot
// and Deposit are what its run entered, or what its dry run would have entered.
internal sealed record BatchMember
{
    public required ulong ContentId { get; init; }

    public required string Name { get; init; }

    public required uint HomeWorldId { get; init; }

    public required CharacterRunSettings Settings { get; init; }

    public RunAssignment? Assignment { get; init; }

    public BatchMemberState State { get; init; }

    public EntryFailure Failure { get; init; }

    public EligibilityReason Reasons { get; init; }

    public PlotKey? Plot { get; init; }

    public long? Deposit { get; init; }

    public DateTime UpdatedUtc { get; init; }

    public bool Waiting => State == BatchMemberState.Queued;
}
