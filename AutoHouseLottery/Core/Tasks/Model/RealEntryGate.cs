using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Tasks.Model;

internal static class EntryCapabilities
{
    // True only once a supervised real entry has confirmed the entry confirmation's contract (Addon rows 7046, 7047 and
    // 7048, the checkbox and the receipt) on the supported build. Until then ordinary use can only dry run.
    public const bool EntryConfirmValidated = false;
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
    // The mode a run starts in, for the badge and the trace. A grant counts only for a run that can reach its plot.
    public static EntryMode ModeFor(bool confirmValidated, bool dryRunRequested, RealEntryGrant? grant, ulong contentId, ReadOnlySpan<PlotKey> targets)
    {
        if (confirmValidated && !dryRunRequested)
        {
            return EntryMode.Real;
        }

        if (grant is not { Used: false } open || open.ContentId != contentId)
        {
            return EntryMode.DryRun;
        }

        for (var targetIndex = 0; targetIndex < targets.Length; targetIndex++)
        {
            if (targets[targetIndex] == open.Plot)
            {
                return EntryMode.Real;
            }
        }

        return EntryMode.DryRun;
    }

    // Asked again with the live plot and deposit right before Confirm. While the capability is not validated, only a
    // grant that names exactly this character, this plot and an amount within its ceiling opens Confirm.
    public static bool MayConfirm(bool confirmValidated, bool dryRunRequested, RealEntryGrant? grant, ulong contentId, PlotKey plot, long deposit)
    {
        if (deposit <= 0)
        {
            return false;
        }

        if (confirmValidated && !dryRunRequested)
        {
            return true;
        }

        return grant is not null && grant.Covers(contentId, plot, deposit);
    }
}
