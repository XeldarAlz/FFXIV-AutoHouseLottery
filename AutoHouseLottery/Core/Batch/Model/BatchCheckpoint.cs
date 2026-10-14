using System.Collections.Immutable;
using AutoHouseLottery.Core.Selection.Model;

namespace AutoHouseLottery.Core.Batch.Model;

// Running: a member's entry run holds the execution lease. Switching: the batch asked Lifestream to log in the next
// member and holds the lease until that character is confirmed. WaitingForLogin: switching is not allowed, so the rest
// wait for their own normal login and nothing is held. Halted: stopped for a reason, resumable by the player. Finished:
// nothing is left to do.
internal enum BatchPhase : byte { Running, Switching, WaitingForLogin, Halted, Finished }

internal enum BatchHalt : byte
{
    None,
    StoppedByPlayer,
    Interrupted,
    CharacterChanged,
    WrongCharacter,
    SwitchFailed,
    NeedsReconciliation,
    JournalUnavailable,
    CheckpointNotSaved,
    Reloaded,
    CycleEnded,
    AutoAnswerPlugin,
}

// How a member's run ended, as the controller saw it: on its own, by the player's Stop, or cut short by a logout, an
// unload or an alert.
internal enum BatchEnd : byte { Finished, StoppedByPlayer, Interrupted }

// A batch as written to disk before every character switch: the frozen policy and ranking it was started with, the dry-run
// choice, every selected character in processing order with its assignment and outcome, and where the batch stands. It
// holds no pointer and no game context, so whatever reads it back after a login rebuilds the character's state from the
// journal and fresh reads.
internal sealed record BatchCheckpoint
{
    public required Guid BatchId { get; init; }

    public required RunPolicy Policy { get; init; }

    public required ImmutableArray<RankedPlot> Ranking { get; init; }

    public required bool DryRunRequested { get; init; }

    public required ImmutableArray<BatchMember> Members { get; init; }

    public BatchPhase Phase { get; init; }

    public BatchHalt Halt { get; init; }

    public ulong? SwitchTarget { get; init; }

    public DateTime CreatedUtc { get; init; }

    public DateTime UpdatedUtc { get; init; }

    public bool Active => Phase is BatchPhase.Running or BatchPhase.Switching;

    public int IndexOf(ulong contentId)
    {
        for (var index = 0; index < Members.Length; index++)
        {
            if (Members[index].ContentId == contentId)
            {
                return index;
            }
        }

        return -1;
    }
}
