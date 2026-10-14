using AutoHouseLottery.Core.Persistence.Model;

namespace AutoHouseLottery.Core.Settlement.Model;

// What the placard showed besides the missing result, read only once its sale data had arrived. Unowned: it shows an
// unowned plot. AllLabelsKnown: every visible button that carries a label reads as one of the known placard rows.
// LotteryButtonShown: at least one of them is a lottery button.
internal readonly record struct PlacardEvidence(bool SaleRead, bool Unowned, bool AllLabelsKnown, bool LotteryButtonShown)
{
    public bool Strong => SaleRead && Unowned && AllLabelsKnown && LotteryButtonShown;
}

// Validated: AbsenceSettlementValidated. CurrentCycle: the receipt's own results period is running now.
// ReadingShowsNoEntry: a fresh reading of the validated lottery record shows no entry at all. SeenInAnotherSession: a
// visit in an earlier session already found the same strong absence. ClaimDeadlinePassed: a win's results period is over.
internal readonly record struct AbsenceEvidence(
    bool Validated,
    PlacardEvidence Placard,
    bool CurrentCycle,
    bool ReadingShowsNoEntry,
    bool SeenInAnotherSession,
    bool ClaimDeadlinePassed);

internal static class AbsenceRules
{
    // A placard that no longer offers this character a result settles the entry only once that rule has been
    // validated in game, and then only on the strongest evidence: a fully read placard of an unowned plot whose every
    // button is known; this cycle's entry confirmed gone by the lottery record, or an earlier cycle's entry found the same
    // way in two different sessions; and never a win before its claim deadline. Anything less keeps the entry pending.
    public static bool Settles(in AbsenceEvidence evidence, SettlementState recorded)
    {
        if (!evidence.Validated || !evidence.Placard.Strong)
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
