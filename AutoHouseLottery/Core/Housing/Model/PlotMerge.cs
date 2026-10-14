using System.Collections.Immutable;

namespace AutoHouseLottery.Core.Housing.Model;

internal static class PlotMerge
{
    private const int FutureToleranceMinutes = 60;
    private const int ContradictionWindowMinutes = 5;

    private const int PublicFeedRank = 1;
    private const int WardScanRank = 2;
    private const int PlacardRank = 3;

    private static readonly TimeSpan futureTolerance = TimeSpan.FromMinutes(FutureToleranceMinutes);
    private static readonly TimeSpan contradictionWindow = TimeSpan.FromMinutes(ContradictionWindowMinutes);

    public static MergeRejection Validate(in PlotObservation observation)
    {
        var provenance = observation.Provenance;
        if (!observation.Key.IsValid)
        {
            return MergeRejection.InvalidAddress;
        }

        if (provenance.Source == ObservationSource.Unknown)
        {
            return MergeRejection.UnknownSource;
        }

        if (!IsUtc(provenance.SourceTimeUtc) || !IsUtc(provenance.ReceivedUtc))
        {
            return MergeRejection.InvalidTimestamp;
        }

        if (provenance.SourceTimeUtc > provenance.ReceivedUtc + futureTolerance)
        {
            return MergeRejection.FutureTimestamp;
        }

        if (provenance.Cycle is { } cycle && cycle.Region != observation.Key.Region)
        {
            return MergeRejection.IdentityMismatch;
        }

        if (observation.Price is <= 0)
        {
            return MergeRejection.InvalidPrice;
        }

        if (observation.EntryCount is < 0)
        {
            return MergeRejection.NegativeEntryCount;
        }

        if (observation.Occupancy == PlotOccupancy.Unknown
            || observation.Size == PlotSize.Unknown
            || observation.Buyers == PlotBuyers.Unknown
            || observation.Sale is { Sale: PlotSale.Unknown })
        {
            return MergeRejection.UnknownValue;
        }

        if (observation.Occupancy == PlotOccupancy.Occupied && observation.Sale is { } offered && IsOffered(offered.Sale))
        {
            return MergeRejection.ContradictoryFields;
        }

        var needsCycle = observation.EntryCount is not null || observation.Sale is { } sale && IsCycleScoped(sale.Sale);
        return needsCycle && provenance.Cycle is null ? MergeRejection.MissingCycle : MergeRejection.None;
    }

    public static MergeRejection TryMerge(PlotSnapshot? current, in PlotObservation observation, out PlotSnapshot merged)
    {
        merged = current ?? new PlotSnapshot(observation.Key);
        var rejection = Validate(observation);
        if (rejection != MergeRejection.None)
        {
            return rejection;
        }

        if (merged.Key != observation.Key)
        {
            return MergeRejection.IdentityMismatch;
        }

        var provenance = observation.Provenance;
        var conflicts = merged.Conflicts;
        var occupancy = MergeDiscrete(merged.Occupancy, observation.Occupancy, provenance, PlotField.Occupancy, static value => (byte)value, ref conflicts);
        var sale = MergeDiscrete(merged.Sale, observation.Sale, provenance, PlotField.Sale, static value => (byte)value.Sale, ref conflicts);
        var size = MergeDiscrete(merged.Size, observation.Size, provenance, PlotField.Size, static value => (byte)value, ref conflicts);
        var buyers = MergeDiscrete(merged.Buyers, observation.Buyers, provenance, PlotField.Buyers, static value => (byte)value, ref conflicts);
        if (observation.Occupancy is not null || observation.Sale is not null)
        {
            conflicts = Resolve(conflicts, PlotField.Availability, provenance);
            conflicts = CheckOccupancyAgainstSale(occupancy, sale, conflicts);
        }

        merged = merged with
        {
            Occupancy = occupancy,
            Sale = sale,
            Size = size,
            Buyers = buyers,
            Price = MergeLatest(merged.Price, observation.Price, provenance),
            EntryCount = MergeEntryCount(merged.EntryCount, observation.EntryCount, provenance),
            Conflicts = conflicts,
        };
        return MergeRejection.None;
    }

    public static bool TryMarkNotReported(PlotSnapshot current, in Provenance omission, out PlotSnapshot marked)
    {
        marked = current;
        if (current.Sale is not { } sale || sale.Value.Sale == PlotSale.NotReported || !Supersedes(omission, sale.Provenance))
        {
            return false;
        }

        marked = current with { Sale = new Observed<SaleState>(new SaleState(PlotSale.NotReported, null), omission) };
        return true;
    }

    public static PlotSnapshot ReleaseOmission(PlotSnapshot current, ObservationSource source, DateTime relistedFromUtc)
    {
        if (current.Sale is not { Value.Sale: PlotSale.NotReported } omission
            || omission.Provenance.Source != source
            || omission.Provenance.SourceTimeUtc >= relistedFromUtc)
        {
            return current;
        }

        return current with { Sale = null };
    }

    public static bool Supersedes(in Provenance incoming, in Provenance existing)
    {
        if (incoming.SourceTimeUtc != existing.SourceTimeUtc)
        {
            return incoming.SourceTimeUtc > existing.SourceTimeUtc;
        }

        return Rank(incoming.Source) >= Rank(existing.Source);
    }

    private static Observed<T>? MergeDiscrete<T>(
        Observed<T>? existing,
        T? incoming,
        in Provenance provenance,
        PlotField field,
        Func<T, byte> encode,
        ref ImmutableArray<FieldConflict> conflicts) where T : struct
    {
        if (incoming is not { } value)
        {
            return existing;
        }

        var candidate = new Observed<T>(value, provenance);
        if (existing is not { } current)
        {
            return candidate;
        }

        var agrees = encode(current.Value) == encode(value);
        if (agrees || !Contradicts(current.Provenance, provenance))
        {
            if (!Supersedes(provenance, current.Provenance))
            {
                return current;
            }

            conflicts = Resolve(conflicts, field, provenance);
            return candidate;
        }

        var keepIncoming = Rank(provenance.Source) > Rank(current.Provenance.Source);
        var kept = keepIncoming ? candidate : current;
        var other = keepIncoming ? current : candidate;
        conflicts = AddConflict(conflicts, new FieldConflict(field, encode(kept.Value), kept.Provenance, encode(other.Value), other.Provenance));
        return kept;
    }

    private static Observed<T>? MergeLatest<T>(Observed<T>? existing, T? incoming, in Provenance provenance) where T : struct
    {
        if (incoming is not { } value)
        {
            return existing;
        }

        if (existing is { } current && !Supersedes(provenance, current.Provenance))
        {
            return current;
        }

        return new Observed<T>(value, provenance);
    }

    private static Observed<int>? MergeEntryCount(Observed<int>? existing, int? incoming, in Provenance provenance)
    {
        if (existing is { Provenance.Cycle: { } existingCycle } && provenance.Cycle is { } newerCycle && newerCycle.IsLaterThan(existingCycle))
        {
            existing = null;
        }

        if (incoming is not { } count || provenance.Cycle is not { } incomingCycle)
        {
            return existing;
        }

        if (existing is { Provenance.Cycle: { } keptCycle } kept
            && (keptCycle.IsLaterThan(incomingCycle) || !Supersedes(provenance, kept.Provenance)))
        {
            return kept;
        }

        return new Observed<int>(count, provenance);
    }

    private static ImmutableArray<FieldConflict> CheckOccupancyAgainstSale(
        Observed<PlotOccupancy>? occupancy,
        Observed<SaleState>? sale,
        ImmutableArray<FieldConflict> conflicts)
    {
        if (occupancy is not { Value: PlotOccupancy.Occupied } occupied || sale is not { } listed || listed.Value.Sale == PlotSale.NotReported)
        {
            return conflicts;
        }

        if (!Contradicts(occupied.Provenance, listed.Provenance))
        {
            return conflicts;
        }

        return AddConflict(
            conflicts,
            new FieldConflict(PlotField.Availability, (byte)PlotOccupancy.Occupied, occupied.Provenance, (byte)listed.Value.Sale, listed.Provenance));
    }

    private static ImmutableArray<FieldConflict> AddConflict(ImmutableArray<FieldConflict> conflicts, in FieldConflict conflict)
    {
        for (var index = 0; index < conflicts.Length; index++)
        {
            var existing = conflicts[index];
            if (existing.Field == conflict.Field && existing.Kept.Source == conflict.Kept.Source && existing.Other.Source == conflict.Other.Source)
            {
                return existing == conflict ? conflicts : conflicts.SetItem(index, conflict);
            }
        }

        return conflicts.Add(conflict);
    }

    private static bool Contradicts(in Provenance left, in Provenance right)
    {
        if (left.Source == right.Source || (left.SourceTimeUtc - right.SourceTimeUtc).Duration() > contradictionWindow)
        {
            return false;
        }

        var newer = left.SourceTimeUtc > right.SourceTimeUtc ? left : right;
        var older = left.SourceTimeUtc > right.SourceTimeUtc ? right : left;
        return newer.Source != ObservationSource.Placard || newer.SourceTimeUtc == older.SourceTimeUtc;
    }

    private static ImmutableArray<FieldConflict> Resolve(ImmutableArray<FieldConflict> conflicts, PlotField field, in Provenance provenance)
    {
        var resolved = conflicts;
        for (var index = conflicts.Length - 1; index >= 0; index--)
        {
            var conflict = conflicts[index];
            if (conflict.Field != field)
            {
                continue;
            }

            var latest = conflict.Kept.SourceTimeUtc > conflict.Other.SourceTimeUtc ? conflict.Kept.SourceTimeUtc : conflict.Other.SourceTimeUtc;
            var settles = provenance.SourceTimeUtc > latest
                && (provenance.Source == ObservationSource.Placard || provenance.SourceTimeUtc - latest > contradictionWindow);
            if (settles)
            {
                resolved = resolved.RemoveAt(index);
            }
        }

        return resolved;
    }

    private static bool IsOffered(PlotSale sale) => sale is PlotSale.LotteryEntry or PlotSale.LotteryResults or PlotSale.FirstComeOpen;

    private static bool IsCycleScoped(PlotSale sale) => sale is PlotSale.Unavailable or PlotSale.LotteryEntry or PlotSale.LotteryResults;

    private static bool IsUtc(DateTime time) => time != default && time.Kind == DateTimeKind.Utc;

    private static int Rank(ObservationSource source) => source switch
    {
        ObservationSource.PublicFeed => PublicFeedRank,
        ObservationSource.WardScan => WardScanRank,
        ObservationSource.Placard => PlacardRank,
        _ => 0,
    };
}
