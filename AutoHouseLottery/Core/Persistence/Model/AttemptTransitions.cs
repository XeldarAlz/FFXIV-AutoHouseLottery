namespace AutoHouseLottery.Core.Persistence.Model;

internal static class AttemptTransitions
{
    public static bool IsLegal(AttemptState from, AttemptState to) => from switch
    {
        AttemptState.Prepared => to is AttemptState.Dispatched or AttemptState.Rejected or AttemptState.NeedsReconciliation,
        AttemptState.Dispatched => to is AttemptState.Confirmed or AttemptState.Rejected or AttemptState.NeedsReconciliation,
        AttemptState.NeedsReconciliation => to is AttemptState.Confirmed or AttemptState.Rejected,
        _ => false,
    };

    public static bool IsUncertain(AttemptState state) => state is AttemptState.Prepared or AttemptState.Dispatched;

    public static bool HoldsSlot(AttemptState state) => state != AttemptState.Rejected;
}
