namespace AutoHouseLottery.Core.Settlement.Model;

// A character the refund queue could switch to. Listed: the player selected it in the character roster. DueCount: the
// result checks and refunds the scheduler would do for it, from the journal alone.
internal readonly record struct RefundCandidate(ulong ContentId, bool Listed, bool AutomaticRefunds, int DueCount);

internal static class RefundSwitchRules
{
    // The session may be left for another character on the same terms background work may start on: the player has left
    // the game alone long enough and nothing else holds it.
    public static bool SessionFree(in SchedulerFacts facts)
        => facts.LoggedIn
        && facts.JournalWritable
        && !facts.AutoAnswerLoaded
        && !facts.InDuty
        && !facts.InCombat
        && !facts.InCutscene
        && !facts.Busy
        && !facts.Occupied
        && !facts.Typing
        && facts.IdleMs >= SchedulerRules.IdleMsRequired;

    // The next character to switch to for its refunds, or null. Only with the explicit option on and switching allowed,
    // only once the logged-in character has nothing left it can do, only a character the player listed whose own automatic
    // refunds are on and that has work due, and each at most once a session so two characters never trade places forever.
    public static ulong? Choose(bool optionOn, bool switchingAllowed, ulong currentContentId, bool currentHasWork, ReadOnlySpan<RefundCandidate> candidates, IReadOnlySet<ulong> visited)
    {
        if (!optionOn || !switchingAllowed || currentHasWork)
        {
            return null;
        }

        for (var index = 0; index < candidates.Length; index++)
        {
            var candidate = candidates[index];
            if (candidate.ContentId != currentContentId
                && candidate.Listed
                && candidate.AutomaticRefunds
                && candidate.DueCount > 0
                && !visited.Contains(candidate.ContentId))
            {
                return candidate.ContentId;
            }
        }

        return null;
    }
}
