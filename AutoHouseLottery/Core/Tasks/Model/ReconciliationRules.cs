using AutoHouseLottery.Core.Game.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Persistence.Model;

namespace AutoHouseLottery.Core.Tasks.Model;

internal enum ReconciliationVerdict : byte { Confirmed, Rejected, StillUncertain }

internal readonly record struct ReconciliationFacts(
    ActionAttempt Attempt,
    CyclePosition Position,
    LotterySnapshot Reading,
    bool? ReadingNamesPlot,
    bool ReadingInCyclePhase);

internal static class ReconciliationRules
{
    // A pending attempt is read back against the character's own fresh lottery reading for the attempt's cycle. Only
    // an entry on the attempted plot confirms it; only an entry elsewhere or the validated record's "no entry" rejects
    // it, since the game allows one entry a period. Everything else stays uncertain and keeps the slot.
    public static ReconciliationVerdict Decide(in ReconciliationFacts facts)
    {
        var attempt = facts.Attempt;
        if (attempt.Kind != ActionKind.Entry
            || !AttemptTransitions.HoldsSlot(attempt.State)
            || attempt.State == AttemptState.Confirmed
            || facts.Reading.ContentId != attempt.CharacterContentId
            || facts.Position.Cycle != attempt.Cycle)
        {
            return ReconciliationVerdict.StillUncertain;
        }

        switch (facts.Reading.State)
        {
            case LotterySnapshotState.LoadedNone:
                return ReconciliationVerdict.Rejected;
            case LotterySnapshotState.Active when facts.ReadingInCyclePhase && facts.ReadingNamesPlot == true:
                return ReconciliationVerdict.Confirmed;
            case LotterySnapshotState.Active when facts.ReadingInCyclePhase && facts.ReadingNamesPlot == false:
                return ReconciliationVerdict.Rejected;
            default:
                return ReconciliationVerdict.StillUncertain;
        }
    }
}
