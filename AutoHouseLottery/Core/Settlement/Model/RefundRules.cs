using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Shared;

namespace AutoHouseLottery.Core.Settlement.Model;

// Matches: the offered refund is what the recorded deposit implies. DepositUnknown: no deposit was recorded (an entry
// found in Timers), so the game's offer is the only amount there is. DiffersFromDeposit: the offer is not what the
// deposit implies, and the plugin cannot tell why.
internal enum RefundOffer : byte { Matches, DepositUnknown, DiffersFromDeposit }

// Everything seen after Yes on a refund: the amount the dialog offered, the gil before the press and the latest gil,
// whether the result dialog is gone, and whether a re-read of the same placard, whose View Lottery Results label matched
// earlier in this visit, still offers a result (null when it could not be re-read). Looks counts the re-reads so far.
internal readonly record struct RefundEvidence(long Offered, long GilBefore, long? GilAfter, bool DialogGone, bool? PlacardStillOffers)
{
    public int Looks { get; init; } = 1;
}

internal enum RefundVerdict : byte { Refunded, Uncertain }

internal readonly record struct RefundSettlement(RefundVerdict Verdict, long? Received);

internal static class RefundRules
{
    private const int CancellationFeePercent = 50;
    private const int MaximumLooks = 2;

    // A losing entry is refunded in full; a forfeited win keeps half after the 50% fee, rounded either way.
    public static RefundOffer CheckOffer(RefundKind kind, long offered, long? deposit)
    {
        if (deposit is not { } paid || paid <= 0)
        {
            return RefundOffer.DepositUnknown;
        }

        return kind switch
        {
            RefundKind.Loss when offered == paid => RefundOffer.Matches,
            RefundKind.Forfeit when IsAfterFee(offered, paid) => RefundOffer.Matches,
            _ => RefundOffer.DiffersFromDeposit,
        };
    }

    // Settled needs the gil to have risen by exactly the offered amount, the dialog to be gone and the same placard to
    // offer no result any more. A gil rise alone never settles a refund, and neither does a rise of any other amount: in
    // the minutes a visit waits, a trade or a sale can move the gil too.
    public static RefundSettlement Settle(in RefundEvidence evidence)
    {
        if (!evidence.DialogGone
            || evidence.PlacardStillOffers != false
            || evidence.GilAfter is not { } after
            || !Gil.TrySubtract(after, evidence.GilBefore, out var received)
            || received != evidence.Offered)
        {
            return new RefundSettlement(RefundVerdict.Uncertain, null);
        }

        return new RefundSettlement(RefundVerdict.Refunded, received);
    }

    // The gil rose and the dialog is gone, but the refund is not settled on the first look: the placard re-read so soon
    // still offers the result or could not be read, or the rise is not exactly the offer. One more look after a further
    // delay is what can still settle it; after that it is left for reconciliation.
    public static bool NeedsSecondLook(in RefundEvidence evidence)
        => evidence.Looks < MaximumLooks
            && evidence.DialogGone
            && evidence.GilAfter is { } after
            && after > evidence.GilBefore
            && Settle(evidence).Verdict == RefundVerdict.Uncertain;

    // A re-read placard can speak about the result once its sale data has arrived, or, for a plot the winner has already
    // claimed (which carries no sale data), once its housing type is known and every labelled button is a known one.
    public static bool PlacardReadable(bool saleRead, bool housingTypeKnown, bool allLabelsKnown) => saleRead || (housingTypeKnown && allLabelsKnown);

    private static bool IsAfterFee(long offered, long paid)
    {
        if (!Gil.TryPercentFloor(paid, CancellationFeePercent, out var feeFloor))
        {
            return false;
        }

        return offered == feeFloor || offered == paid - feeFloor;
    }
}
