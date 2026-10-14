using AutoHouseLottery.Core.Selection.Model;
using AutoHouseLottery.Core.Tasks.Model;

namespace AutoHouseLottery.Core.Batch.Model;

internal static class BatchCapabilities
{
    // True only once supervised batches have shown, on the supported build, that several characters' real entries go
    // through one after another with the same receipts, reservations and recovery as a single real entry. Until then, and
    // until EntryCapabilities.EntryConfirmValidated, every batch is a dry run.
    public const bool BatchEntryValidated = false;
}

// A batch goes real only when both the single entry and the batch have been validated in game, and its own dry-run choice
// allows it. A maintainer's one-plot grant never makes a batch real: a batch member runs as a dry run whatever is granted.
internal static class BatchGate
{
    public static RunMode ModeOf(in RunAssignment assignment) => assignment.Exact ? RunMode.Exact : RunMode.BestMatch;

    // What each member's entry run is told about its dry run. A run told it is a dry run never presses Confirm and never
    // collects an earlier refund for real.
    public static bool MemberDryRun(bool confirmValidated, bool batchValidated, bool dryRunRequested)
        => dryRunRequested || !confirmValidated || !batchValidated;

    public static EntryMode EntryModeFor(bool confirmValidated, bool batchValidated, bool dryRunRequested)
        => MemberDryRun(confirmValidated, batchValidated, dryRunRequested) ? EntryMode.DryRun : EntryMode.Real;
}
