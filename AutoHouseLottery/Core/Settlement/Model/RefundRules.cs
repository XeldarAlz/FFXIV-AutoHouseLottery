using AutoHouseLottery.Core.Persistence.Model;
using AutoHouseLottery.Core.Shared;

namespace AutoHouseLottery.Core.Settlement.Model;

// Matches: the offered refund is what the recorded deposit implies. DepositUnknown: no deposit was recorded (an entry
// found in Timers), so the game's offer is the only amount there is. DiffersFromDeposit: the offer is not what the
// deposit implies, and the plugin cannot tell why.
internal enum RefundOffer : byte { Matches, DepositUnknown, DiffersFromDeposit }

// Everything seen after Yes on a refund: the amount the dialog offered, the gil before the press and afterwards, and
// whether a fresh lottery reading still shows the entry (null when no fresh reading could say).
internal readonly record struct RefundEvidence(long Offered, long GilBefore, long? GilAfter, bool? EntryShown);

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

    // Settled needs both a fresh reading that no longer shows the entry and the gil to have risen; a gil rise alone never
    // settles a refund. A rise other than the offered amount is recorded as received, never as the full refund.
    public static RefundSettlement Settle(in RefundEvidence evidence)
    {
        if (evidence.EntryShown != false
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

    private static bool IsAfterFee(long offered, long paid)
    {
        if (!Gil.TryPercentFloor(paid, CancellationFeePercent, out var feeFloor))
        {
            return false;
        }

        return offered == feeFloor || offered == paid - feeFloor;
    }
}
