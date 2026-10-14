using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Providers.Model;

// The few cycle positions the listings and the placards proved, oldest first, and the rules for keeping them. The cycle
// position anyone uses comes from these through the cycle resolver, which reads the newest first.
internal static class CyclePositions
{
    public const int MaximumKept = 8;

    // New positions join at the end; one already kept is not added twice, and past the limit the oldest goes.
    public static void Remember(List<CyclePosition> kept, ReadOnlySpan<CyclePosition> proved)
    {
        for (var index = 0; index < proved.Length; index++)
        {
            if (kept.Contains(proved[index]))
            {
                continue;
            }

            kept.Add(proved[index]);
            if (kept.Count > MaximumKept)
            {
                kept.RemoveAt(0);
            }
        }
    }

    // Only a calendar that was established or moved is written out; an unchanged or rejected one leaves the file alone.
    public static bool Persists(ScheduleUpdate update) => update is ScheduleUpdate.Established or ScheduleUpdate.Reanchored;
}
