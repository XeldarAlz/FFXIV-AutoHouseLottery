using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Tasks.Model;

internal static class EntryCapabilities
{
    // True only once a supervised real entry has confirmed the entry confirmation's contract (Addon rows 7046, 7047 and
    // 7048, the checkbox and the receipt) on the supported build. Until then ordinary use can only dry run.
    public const bool EntryConfirmValidated = false;

    // A player's Enter asks for a dry run until the entry confirmation has been validated, and for a real entry after.
    public static bool PlayerDryRun => !EntryConfirmValidated;
}

// A maintainer's permission for one real entry: one character, one plot, a deposit ceiling. It lives in memory only,
// so it ends with the session, at logout, and after the one entry it was given for.
internal sealed record RealEntryGrant(ulong ContentId, string CharacterName, PlotKey Plot, long MaxDeposit, DateTime IssuedUtc)
{
    public bool Used { get; init; }

    public bool Covers(ulong contentId, PlotKey plot, long deposit)
        => !Used && contentId == ContentId && plot == Plot && deposit > 0 && deposit <= MaxDeposit;
}

internal static class RealEntryGate
{
    // The mode a run starts in, for the badge and the trace. A requested dry run always stays one. A grant counts only
    // for an exact-plot run on its own plot, never for a best match.
    public static EntryMode ModeFor(bool confirmValidated, bool dryRunRequested, RealEntryGrant? grant, RunMode mode, ulong contentId, ReadOnlySpan<PlotKey> targets)
    {
        if (dryRunRequested)
        {
            return EntryMode.DryRun;
        }

        if (confirmValidated)
        {
            return EntryMode.Real;
        }

        return mode == RunMode.Exact && targets.Length == 1 && grant is { Used: false } open && open.ContentId == contentId && open.Plot == targets[0]
            ? EntryMode.Real
            : EntryMode.DryRun;
    }

    // Asked again with the live plot and deposit right before Confirm. A requested dry run never confirms. While the
    // capability is not validated, only a grant that names exactly this character, this plot and an amount within its
    // ceiling opens Confirm, and only for an exact-plot run.
    public static bool MayConfirm(bool confirmValidated, bool dryRunRequested, RealEntryGrant? grant, RunMode mode, ulong contentId, PlotKey plot, long deposit)
    {
        if (dryRunRequested || deposit <= 0)
        {
            return false;
        }

        if (confirmValidated)
        {
            return true;
        }

        return mode == RunMode.Exact && grant is not null && grant.Covers(contentId, plot, deposit);
    }
}
