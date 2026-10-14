using System.Collections.Immutable;
using System.Text.Json;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Providers.Model;

// The service relays PaissaDB, whose wards and plots are zero-based. They become the canonical one-based address here and
// nowhere else. A row that cannot be read exactly is rejected rather than repaired, and a district with a rejected row
// is reported as partial so the plots it may have dropped are never taken for unlisted. A response with nothing usable in
// it is malformed, never an empty listing.
//
// Only rows that were read cleanly, and that were observed during the phase they report, vote on where the lottery
// calendar stands; the phase end most of them agree on may move the region's calendar, and only on a live response. A row
// whose own phase end is off that calendar keeps its plot but loses the fields that belong to a cycle.
internal static class FeedNormalizer
{
    public const int GridToleranceMinutes = 60;

    private const int LotteryFlag = 1;
    private const int FreeCompanyFlag = 2;
    private const int PrivateFlag = 4;
    private const int KnownFlags = LotteryFlag | FreeCompanyFlag | PrivateFlag;

    private const int EntryPhase = 1;
    private const int ResultsPhase = 2;
    private const int UnavailablePhase = 3;

    private const int SmallSize = 0;
    private const int MediumSize = 1;
    private const int LargeSize = 2;

    private static readonly TimeSpan gridTolerance = TimeSpan.FromMinutes(GridToleranceMinutes);

    public static bool TryParseWorld(ReadOnlySpan<byte> body, out FeedWorldDetail? detail)
    {
        try
        {
            detail = JsonSerializer.Deserialize(body, FeedJsonContext.Default.FeedWorldDetail);
            return detail is not null;
        }
        catch (JsonException)
        {
            detail = null;
            return false;
        }
    }

    public static ImmutableArray<FeedWorld> ParseWorlds(ReadOnlySpan<byte> body)
    {
        FeedWorldSummary[]? summaries;
        try
        {
            summaries = JsonSerializer.Deserialize(body, FeedJsonContext.Default.FeedWorldSummaryArray);
        }
        catch (JsonException)
        {
            return [];
        }

        if (summaries is null)
        {
            return [];
        }

        var worlds = ImmutableArray.CreateBuilder<FeedWorld>(summaries.Length);
        for (var index = 0; index < summaries.Length; index++)
        {
            if (summaries[index] is not { Id: { } id } summary || id == 0 || string.IsNullOrWhiteSpace(summary.Name))
            {
                continue;
            }

            worlds.Add(new FeedWorld(id, summary.Name, summary.DataCenterName ?? string.Empty));
        }

        return worlds.ToImmutable();
    }

    public static FeedResult Normalize(FeedWorldDetail? payload, uint requestedWorldId, in FeedReceipt receipt, LotterySchedules schedules, CalendarUse calendar)
    {
        if (payload?.Districts is not { } districts)
        {
            return FeedResult.Failed(FeedProblem.Malformed);
        }

        if (payload.Id != requestedWorldId)
        {
            return FeedResult.Failed(FeedProblem.WrongWorld);
        }

        var readings = Read(districts, requestedWorldId, receipt);
        if (!Usable(readings))
        {
            return FeedResult.Failed(FeedProblem.Malformed);
        }

        var update = calendar == CalendarUse.Anchor ? Anchor(readings, receipt, schedules) : ScheduleUpdate.Unchanged;
        schedules.TryGet(GameRegion.Global, out var schedule);
        var built = ImmutableArray.CreateBuilder<DistrictFeed>(readings.Count);
        var positions = ImmutableArray.CreateBuilder<CyclePosition>();
        for (var index = 0; index < readings.Count; index++)
        {
            built.Add(Build(readings[index], receipt, schedule, positions));
        }

        return new FeedResult(FeedProblem.None, new WorldFeed(requestedWorldId, receipt, built.ToImmutable(), positions.ToImmutable(), update, schedule));
    }

    // Usable means at least one accepted row, or at least one recognised district that is completely and honestly empty.
    private static bool Usable(List<DistrictReading> readings)
    {
        for (var index = 0; index < readings.Count; index++)
        {
            var reading = readings[index];
            if (reading.Rows.Count > 0 || (reading.Rejected == 0 && reading.Detail is { OpenPlots.Length: 0, OpenPlotCount: 0 }))
            {
                return true;
            }
        }

        return false;
    }

    private static List<DistrictReading> Read(FeedDistrictDetail?[] districts, uint worldId, in FeedReceipt receipt)
    {
        var readings = new List<DistrictReading>(HousingDistricts.All.Length);
        var seen = new HashSet<uint>();
        for (var index = 0; index < districts.Length; index++)
        {
            if (districts[index] is not { Id: { } districtId } detail
                || !HousingDistricts.TryGet(districtId, out _)
                || !seen.Add(districtId)
                || !CoverageScope.TryForDistrict(GameRegion.Global, worldId, districtId, out var scope))
            {
                continue;
            }

            var rows = detail.OpenPlots ?? [];
            var accepted = new List<RowReading>(rows.Length);
            var keys = new HashSet<PlotKey>(rows.Length);
            var rejected = 0;
            for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
            {
                if (!TryReadRow(rows[rowIndex], worldId, districtId, receipt, out var reading) || !keys.Add(reading.Plain.Key))
                {
                    rejected++;
                    continue;
                }

                accepted.Add(reading);
            }

            readings.Add(new DistrictReading(detail, scope, accepted, rejected));
        }

        return readings;
    }

    // Everything about a row that does not depend on the lottery calendar: its address, its source time and the plain
    // fields, checked by the same rules the plot store applies.
    private static bool TryReadRow(FeedOpenPlot? row, uint worldId, uint districtId, in FeedReceipt receipt, out RowReading reading)
    {
        reading = default;
        if (row is null || row.WorldId != worldId || row.DistrictId != districtId)
        {
            return false;
        }

        if (row.WardNumber is not { } wardIndex || row.PlotNumber is not { } plotIndex || wardIndex < 0 || plotIndex < 0)
        {
            return false;
        }

        if (!PlotKey.TryCreate(GameRegion.Global, worldId, districtId, wardIndex + 1, plotIndex + 1, out var key))
        {
            return false;
        }

        if (row.LotteryEntries is < 0 || !UnixTime.TryFromSeconds(row.LastUpdatedTime, out var sourceTime))
        {
            return false;
        }

        var flags = row.PurchaseSystem is { } purchase && purchase > 0 && (purchase & ~KnownFlags) == 0 ? purchase : 0;
        var plain = new PlotObservation(key, new Provenance(ObservationSource.PublicFeed, sourceTime, receipt.ReceivedUtc, null))
        {
            Occupancy = PlotOccupancy.Vacant,
            Size = MapSize(row.Size),
            Price = row.Price is > 0 ? row.Price : null,
            Buyers = MapBuyers(flags),
        };
        if (PlotMerge.Validate(plain) != MergeRejection.None)
        {
            return false;
        }

        DateTime? phaseEnd = UnixTime.TryFromSeconds(row.LotteryPhaseUntil, out var end) ? end : null;
        reading = new RowReading(plain, flags, row.LotteryPhase, phaseEnd, row.LotteryEntries);
        return true;
    }

    private static ScheduleUpdate Anchor(List<DistrictReading> readings, in FeedReceipt receipt, LotterySchedules schedules)
    {
        var votes = new Dictionary<(LotteryPhase Phase, DateTime End), int>();
        for (var districtIndex = 0; districtIndex < readings.Count; districtIndex++)
        {
            var rows = readings[districtIndex].Rows;
            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                if (Vote(rows[rowIndex], receipt) is { } vote)
                {
                    votes[vote] = votes.GetValueOrDefault(vote) + 1;
                }
            }
        }

        var best = default((LotteryPhase Phase, DateTime End));
        var bestVotes = 0;
        foreach (var vote in votes)
        {
            if (vote.Value > bestVotes || (vote.Value == bestVotes && vote.Key.End > best.End))
            {
                best = vote.Key;
                bestVotes = vote.Value;
            }
        }

        return bestVotes == 0 ? ScheduleUpdate.Unchanged : schedules.Observe(GameRegion.Global, best.Phase, best.End, out _);
    }

    // A row speaks for the calendar only about a phase that had begun when it was seen and had not ended when the service
    // last read its upstream.
    private static (LotteryPhase Phase, DateTime End)? Vote(in RowReading row, in FeedReceipt receipt)
    {
        var phase = row.Phase switch
        {
            EntryPhase => LotteryPhase.Entry,
            ResultsPhase => LotteryPhase.Results,
            _ => LotteryPhase.Unknown,
        };
        if (phase == LotteryPhase.Unknown || row.PhaseEnd is not { } end || end < receipt.UpstreamUtc)
        {
            return null;
        }

        var phaseStart = end - (phase == LotteryPhase.Entry ? LotteryCalendar.EntryPeriod : LotteryCalendar.ResultsPeriod);
        return row.Plain.Provenance.SourceTimeUtc >= phaseStart ? (phase, end) : null;
    }

    private static DistrictFeed Build(DistrictReading reading, in FeedReceipt receipt, in LotterySchedule schedule, ImmutableArray<CyclePosition>.Builder positions)
    {
        var observations = ImmutableArray.CreateBuilder<PlotObservation>(reading.Rows.Count);
        var rejected = reading.Rejected;
        for (var index = 0; index < reading.Rows.Count; index++)
        {
            var observation = WithCycle(reading.Rows[index], schedule, positions);
            if (PlotMerge.Validate(observation) != MergeRejection.None)
            {
                rejected++;
                continue;
            }

            observations.Add(observation);
        }

        var detail = reading.Detail;
        var listed = detail.OpenPlots is not null && detail.OpenPlotCount == detail.OpenPlots.Length;
        var outcome = listed && rejected == 0 ? CoverageOutcome.Complete : CoverageOutcome.Partial;
        var coverage = new CoverageSnapshot(
            reading.Scope,
            ObservationSource.PublicFeed,
            outcome,
            detail.OpenPlotCount,
            observations.Count,
            receipt.UpstreamUtc,
            receipt.UpstreamUtc,
            receipt.ReceivedUtc);
        return new DistrictFeed(coverage, observations.ToImmutable(), rejected);
    }

    private static PlotObservation WithCycle(in RowReading row, in LotterySchedule schedule, ImmutableArray<CyclePosition>.Builder positions)
    {
        var sale = MapSale(row.Flags, row.Phase, row.PhaseEnd);
        var cycle = ResolveCycle(schedule, row.Phase, row.PhaseEnd, positions);
        if (sale is { } scoped && IsCycleScoped(scoped.Sale) && cycle is null)
        {
            sale = null;
        }

        return row.Plain with
        {
            Provenance = row.Plain.Provenance with { Cycle = cycle },
            Sale = sale,
            EntryCount = cycle is null ? null : row.Entries,
        };
    }

    private static SaleState? MapSale(int flags, int? phase, DateTime? phaseEnd)
    {
        if (flags == 0)
        {
            return null;
        }

        if ((flags & LotteryFlag) == 0)
        {
            return new SaleState(PlotSale.FirstComeOpen, null);
        }

        return phase switch
        {
            EntryPhase => new SaleState(PlotSale.LotteryEntry, phaseEnd),
            ResultsPhase => new SaleState(PlotSale.LotteryResults, phaseEnd),
            UnavailablePhase => new SaleState(PlotSale.Unavailable, phaseEnd),
            _ => null,
        };
    }

    // A plot reported as unavailable sits between lotteries: its phase end is either the end of the cycle or the end of
    // the entry period. Only the reading that lands on the lottery calendar is accepted, and only a reported entry or
    // results phase counts as evidence of where the calendar stands.
    private static CycleKey? ResolveCycle(in LotterySchedule schedule, int? phase, DateTime? phaseEnd, ImmutableArray<CyclePosition>.Builder positions)
    {
        if (phaseEnd is not { } end)
        {
            return null;
        }

        return phase switch
        {
            EntryPhase => OnCalendar(schedule, LotteryPhase.Entry, end, positions),
            ResultsPhase => OnCalendar(schedule, LotteryPhase.Results, end, positions),
            UnavailablePhase => OnCalendar(schedule, LotteryPhase.Results, end, null) ?? OnCalendar(schedule, LotteryPhase.Entry, end, null),
            _ => null,
        };
    }

    private static CycleKey? OnCalendar(in LotterySchedule schedule, LotteryPhase phase, DateTime phaseEnd, ImmutableArray<CyclePosition>.Builder? positions)
    {
        if (!LotteryCalendar.TryVerify(schedule, phase, phaseEnd, out var position))
        {
            return null;
        }

        var calendarEnd = LotteryCalendar.EntryStartOf(schedule, position.Cycle.Number) + (phase == LotteryPhase.Entry ? LotteryCalendar.EntryPeriod : LotteryCalendar.CycleLength);
        if ((phaseEnd - calendarEnd).Duration() > gridTolerance)
        {
            return null;
        }

        if (positions is not null && !positions.Contains(position))
        {
            positions.Add(position);
        }

        return position.Cycle;
    }

    private static PlotSize? MapSize(int? size) => size switch
    {
        SmallSize => PlotSize.Small,
        MediumSize => PlotSize.Medium,
        LargeSize => PlotSize.Large,
        _ => null,
    };

    private static PlotBuyers? MapBuyers(int flags)
    {
        var freeCompany = (flags & FreeCompanyFlag) != 0;
        var individual = (flags & PrivateFlag) != 0;
        if (freeCompany && individual)
        {
            return PlotBuyers.Both;
        }

        if (freeCompany)
        {
            return PlotBuyers.FreeCompany;
        }

        return individual ? PlotBuyers.Private : null;
    }

    private static bool IsCycleScoped(PlotSale sale) => sale is PlotSale.Unavailable or PlotSale.LotteryEntry or PlotSale.LotteryResults;

    private readonly record struct RowReading(PlotObservation Plain, int Flags, int? Phase, DateTime? PhaseEnd, int? Entries);

    private sealed record DistrictReading(FeedDistrictDetail Detail, CoverageScope Scope, List<RowReading> Rows, int Rejected);
}
