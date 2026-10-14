namespace AutoHouseLottery.Core.Find.Model;

// Every order ends on the full plot address, so equal rows always land in the same place and the list does not reshuffle
// under the player while they tick plots.
internal static class ResultOrdering
{
    public static Comparison<ResultRow> For(ResultSort sort, uint preferredDistricts) => sort switch
    {
        ResultSort.FewestEntries => static (left, right) => Then(KnownFirst(left.Entries, right.Entries), left, right),
        ResultSort.LowestDeposit => static (left, right) => Then(KnownFirst(left.Price, right.Price), left, right),
        ResultSort.RecentlyObserved => static (left, right) => Then(right.ObservedUtc.CompareTo(left.ObservedUtc), left, right),
        ResultSort.Address => static (left, right) => left.Key.CompareTo(right.Key),
        _ => (left, right) => Then(Preferred(right, preferredDistricts).CompareTo(Preferred(left, preferredDistricts)), left, right),
    };

    private static bool Preferred(in ResultRow row, uint preferredDistricts) => (preferredDistricts & AreaScope.DistrictBit(row.Key.DistrictId)) != 0;

    private static int Then(int comparison, in ResultRow left, in ResultRow right) => comparison != 0 ? comparison : left.Key.CompareTo(right.Key);

    private static int KnownFirst<T>(T? left, T? right) where T : struct, IComparable<T>
    {
        if (left is { } knownLeft)
        {
            return right is { } knownRight ? knownLeft.CompareTo(knownRight) : -1;
        }

        return right is null ? 0 : 1;
    }
}
