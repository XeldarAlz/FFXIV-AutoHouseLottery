using AutoHouseLottery.Core.Persistence.Model;

namespace AutoHouseLottery.Core.Settlement.Model;

// What the placard showed besides the missing result, read only once its sale data had arrived. Unowned: it shows an
// unowned plot. AllLabelsKnown: every visible button that carries a label reads as one of the known placard rows.
// LotteryButtonShown: at least one of them is a lottery button.
internal readonly record struct PlacardEvidence(bool SaleRead, bool Unowned, bool AllLabelsKnown, bool LotteryButtonShown)
{
    public bool Strong => SaleRead && Unowned && AllLabelsKnown && LotteryButtonShown;
}

// CurrentCycle: the receipt's own results period is running now. ReadingShowsNoEntry: a fresh reading of the validated
// lottery record shows no entry at all. SeenInAnotherSession: a visit in an earlier session already found the same strong
// absence. ClaimDeadlinePassed: a win's results period is over.
internal readonly record struct AbsenceEvidence(
    PlacardEvidence Placard,
    bool CurrentCycle,
    bool ReadingShowsNoEntry,
    bool SeenInAnotherSession,
    bool ClaimDeadlinePassed);

internal static class AbsenceRules
{
    // Two sightings count as two sessions only this far apart, so a reload or a quick relog is not a second visit.
    public static readonly TimeSpan MinimumSightingGap = TimeSpan.FromHours(6);

    // An earlier strong sighting from another session, at least the minimum gap before this one.
    public static bool CountsAsSecondSighting(bool earlierStrong, Guid earlierSession, DateTime earlierUtc, Guid session, DateTime nowUtc)
        => earlierStrong && earlierSession != session && nowUtc - earlierUtc >= MinimumSightingGap;

    // A placard that no longer offers this character a result settles the entry only on the strongest evidence: a fully
    // read placard of an unowned plot whose every button is known; this cycle's entry confirmed gone by the lottery
    // record, or an earlier cycle's entry found the same way in two different sessions; and never a win before its claim
    // deadline. Anything less keeps the entry pending.
    public static bool Settles(in AbsenceEvidence evidence, SettlementState recorded)
    {
        if (!evidence.Placard.Strong)
        {
            return false;
        }

        if (recorded == SettlementState.WonUnclaimed && !evidence.ClaimDeadlinePassed)
        {
            return false;
        }

        return evidence.CurrentCycle ? evidence.ReadingShowsNoEntry : evidence.SeenInAnotherSession;
    }
}
