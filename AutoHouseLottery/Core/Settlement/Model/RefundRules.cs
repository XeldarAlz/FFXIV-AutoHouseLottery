using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Shared;

namespace AutoHouseLottery.Core.Settlement.Model;

// Matches: the offered refund is what the recorded deposit implies. DepositUnknown: no deposit was recorded (an entry
// found in Timers), so the game's offer is the only amount there is. DiffersFromDeposit: the offer is not what the
// deposit implies, and the plugin cannot tell why.
internal enum RefundOffer : byte { Matches, DepositUnknown, DiffersFromDeposit }

// Everything seen after Yes on a refund: the amount the dialog offered, the gil before the press and the latest gil, and
// whether a fresh lottery reading still shows the entry (null when no fresh reading could say). SecondEntryShown is the
// same from a reading taken again after a further delay, when one was.
internal readonly record struct RefundEvidence(long Offered, long GilBefore, long? GilAfter, bool? EntryShown)
{
    public bool SecondReadingTaken { get; init; }

    public bool? SecondEntryShown { get; init; }
}

internal enum RefundVerdict : byte { Refunded, RefundedAmountDiffers, Uncertain }

internal readonly record struct RefundSettlement(RefundVerdict Verdict, long? Received);

internal static class RefundRules
{
    private const int CancellationFeePercent = 50;

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

    // Settled needs both the gil to have risen and a fresh reading that no longer shows the entry; a gil rise alone never
    // settles a refund. A reading so soon after the press may not have caught up, so the latest of the two readings
    // decides. A rise other than the offered amount is recorded as received, never as the full refund.
    public static RefundSettlement Settle(in RefundEvidence evidence)
    {
        var latest = evidence.SecondReadingTaken ? evidence.SecondEntryShown : evidence.EntryShown;
        if (latest != false
            || evidence.GilAfter is not { } after
            || !Gil.TrySubtract(after, evidence.GilBefore, out var received)
            || received <= 0)
        {
            return new RefundSettlement(RefundVerdict.Uncertain, null);
        }

        return received == evidence.Offered
            ? new RefundSettlement(RefundVerdict.Refunded, received)
            : new RefundSettlement(RefundVerdict.RefundedAmountDiffers, received);
    }

    // The gil rose but the one reading so far does not show the entry gone: a second reading after a further delay is what
    // can still settle the refund.
    public static bool NeedsSecondReading(in RefundEvidence evidence)
        => !evidence.SecondReadingTaken
            && evidence.EntryShown != false
            && evidence.GilAfter is { } after
            && after > evidence.GilBefore;

    private static bool IsAfterFee(long offered, long paid)
    {
        if (!Gil.TryPercentFloor(paid, CancellationFeePercent, out var feeFloor))
        {
            return false;
        }

        return offered == feeFloor || offered == paid - feeFloor;
    }
}
