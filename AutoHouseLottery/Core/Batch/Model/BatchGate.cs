using AutoHouseLottery.Core.Selection.Model;
using AutoHouseLottery.Core.Tasks.Model;

namespace AutoHouseLottery.Core.Batch.Model;

// A batch runs each character through the single-character gate with that character's own assignment, so batching can
// never widen a grant: only a character whose own exact target is the granted plot can be granted, and a best match or a
// plot pinned by another member's entry always runs as a best match.
internal static class BatchGate
{
    public static RunMode ModeOf(in RunAssignment assignment) => assignment.Exact ? RunMode.Exact : RunMode.BestMatch;

    public static EntryMode EntryModeFor(bool confirmValidated, bool dryRunRequested, RealEntryGrant? grant, in RunAssignment assignment)
        => RealEntryGate.ModeFor(confirmValidated, dryRunRequested, grant, ModeOf(assignment), assignment.ContentId, assignment.TargetOrder.AsSpan());
}
