using System.Collections.Immutable;
using System.Text.Json;
using AutoHouseLottery.Core.Housing.Model;

namespace AutoHouseLottery.Core.Providers.Model;

// The service relays PaissaDB, whose wards and plots are zero-based. They become the canonical one-based address here and
// nowhere else. A row that cannot be read exactly is rejected rather than repaired, and a district with a rejected row
// is reported as partial so the plots it may have dropped are never taken for unlisted. The phase end most rows agree on
// anchors the region's lottery calendar; a row whose own phase end is off that calendar keeps its plot but loses the
// fields that belong to a cycle.
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

    public static FeedResult Normalize(FeedWorldDetail? payload, uint requestedWorldId, in FeedReceipt receipt, LotterySchedules schedules)
    {
        if (payload?.Districts is not { } districts)
        {
            return FeedResult.Failed(FeedProblem.Malformed);
        }

        if (payload.Id != requestedWorldId)
        {
            return FeedResult.Failed(FeedProblem.WrongWorld);
        }

        var update = Anchor(districts, schedules);
        schedules.TryGet(GameRegion.Global, out var schedule);
        var built = ImmutableArray.CreateBuilder<DistrictFeed>(HousingDistricts.All.Length);
        var positions = ImmutableArray.CreateBuilder<CyclePosition>();
        var seen = new HashSet<uint>();
        for (var index = 0; index < districts.Length; index++)
        {
            if (districts[index] is not { Id: { } districtId } detail
                || !HousingDistricts.TryGet(districtId, out _)
                || !seen.Add(districtId)
                || !CoverageScope.TryForDistrict(GameRegion.Global, requestedWorldId, districtId, out var scope))
            {
                continue;
            }

            built.Add(NormalizeDistrict(detail, requestedWorldId, districtId, scope, receipt, schedule, positions));
        }

        return new FeedResult(FeedProblem.None, new WorldFeed(requestedWorldId, receipt, built.ToImmutable(), positions.ToImmutable(), update));
    }

    private static ScheduleUpdate Anchor(FeedDistrictDetail?[] districts, LotterySchedules schedules)
    {
        var votes = new Dictionary<(LotteryPhase Phase, DateTime End), int>();
        for (var districtIndex = 0; districtIndex < districts.Length; districtIndex++)
        {
            var rows = districts[districtIndex]?.OpenPlots ?? [];
            for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
            {
                if (rows[rowIndex] is not { } row || ReportedPhase(row.LotteryPhase) is not { } phase || !UnixTime.TryFromSeconds(row.LotteryPhaseUntil, out var end))
                {
                    continue;
                }

                votes[(phase, end)] = votes.GetValueOrDefault((phase, end)) + 1;
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

    private static LotteryPhase? ReportedPhase(int? phase) => phase switch
    {
        EntryPhase => LotteryPhase.Entry,
        ResultsPhase => LotteryPhase.Results,
        _ => null,
    };

    private static DistrictFeed NormalizeDistrict(
        FeedDistrictDetail detail,
        uint worldId,
        uint districtId,
        in CoverageScope scope,
        in FeedReceipt receipt,
        in LotterySchedule schedule,
        ImmutableArray<CyclePosition>.Builder positions)
    {
        var rows = detail.OpenPlots ?? [];
        var observations = ImmutableArray.CreateBuilder<PlotObservation>(rows.Length);
        var keys = new HashSet<PlotKey>(rows.Length);
        var rejected = 0;
        for (var index = 0; index < rows.Length; index++)
        {
            if (!TryNormalizeRow(rows[index], worldId, districtId, receipt, schedule, positions, out var observation) || !keys.Add(observation.Key))
            {
                rejected++;
                continue;
            }

            observations.Add(observation);
        }

        var listed = detail.OpenPlots is not null && detail.OpenPlotCount == rows.Length;
        var outcome = listed && rejected == 0 ? CoverageOutcome.Complete : CoverageOutcome.Partial;
        var coverage = new CoverageSnapshot(
            scope,
            ObservationSource.PublicFeed,
            outcome,
            detail.OpenPlotCount,
            observations.Count,
            receipt.UpstreamUtc,
            receipt.UpstreamUtc,
            receipt.ReceivedUtc);
        return new DistrictFeed(coverage, observations.ToImmutable(), rejected);
    }

    private static bool TryNormalizeRow(
        FeedOpenPlot? row,
        uint worldId,
        uint districtId,
        in FeedReceipt receipt,
        in LotterySchedule schedule,
        ImmutableArray<CyclePosition>.Builder positions,
        out PlotObservation observation)
    {
        observation = default;
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

        var hasPhaseEnd = UnixTime.TryFromSeconds(row.LotteryPhaseUntil, out var phaseEnd);
        var flags = row.PurchaseSystem is { } purchase && purchase > 0 && (purchase & ~KnownFlags) == 0 ? purchase : 0;
        var sale = MapSale(flags, row.LotteryPhase, hasPhaseEnd ? phaseEnd : null);
        var cycle = ResolveCycle(schedule, row.LotteryPhase, hasPhaseEnd ? phaseEnd : null, positions);
        if (sale is { } scoped && IsCycleScoped(scoped.Sale) && cycle is null)
        {
            sale = null;
        }

        observation = new PlotObservation(key, new Provenance(ObservationSource.PublicFeed, sourceTime, receipt.ReceivedUtc, cycle))
        {
            Occupancy = PlotOccupancy.Vacant,
            Sale = sale,
            Size = MapSize(row.Size),
            Price = row.Price is > 0 ? row.Price : null,
            Buyers = MapBuyers(flags),
            EntryCount = cycle is null ? null : row.LotteryEntries,
        };
        return PlotMerge.Validate(observation) == MergeRejection.None;
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
}
