using System.Text.Json.Serialization;
using AutoHouseLottery.Core.Batch.Model;
using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Selection.Model;
using AutoHouseLottery.Core.Tasks.Model;

namespace AutoHouseLottery.Core.Persistence.Model;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(BatchFileJson))]
internal sealed partial class BatchJsonContext : JsonSerializerContext
{
}

internal readonly struct BatchFileJson
{
    public int? Schema { get; init; }

    public BatchJson? Batch { get; init; }
}

internal readonly struct BatchJson
{
    public Guid? Id { get; init; }

    public DateTime? CreatedUtc { get; init; }

    public DateTime? UpdatedUtc { get; init; }

    public bool? DryRun { get; init; }

    public BatchPhase? Phase { get; init; }

    public BatchHalt? Halt { get; init; }

    public ulong? SwitchTarget { get; init; }

    public PolicyJson? Policy { get; init; }

    public RankedPlotJson[]? Ranking { get; init; }

    public MemberJson[]? Members { get; init; }
}

internal readonly struct PolicyJson
{
    public int? Revision { get; init; }

    public CyclePositionJson? Cycle { get; init; }

    public SettingsJson[]? Characters { get; init; }

    public long? TravelAllowance { get; init; }

    public ShortlistRefJson? Shortlist { get; init; }

    public PlacementChoice? Placement { get; init; }

    public bool? MultipleWinRiskAccepted { get; init; }

    public int? MaxInspections { get; init; }

    public bool? CollectRefundsFirst { get; init; }
}

internal readonly struct CyclePositionJson
{
    public CycleKeyJson? Cycle { get; init; }

    public LotteryPhase? Phase { get; init; }

    public DateTime? StartUtc { get; init; }

    public DateTime? EndUtc { get; init; }

    public int? ScheduleRevision { get; init; }

    public CycleEvidence? Evidence { get; init; }
}

internal readonly struct ShortlistRefJson
{
    public Guid? Id { get; init; }

    public int? Revision { get; init; }

    public PlotKeyJson[]? Plots { get; init; }
}

internal readonly struct SettingsJson
{
    public ulong? Character { get; init; }

    public BuyerType? Buyer { get; init; }

    public long? DepositCap { get; init; }

    public long? Reserve { get; init; }

    public PlotKeyJson? Target { get; init; }
}

internal readonly struct RankedPlotJson
{
    public PlotKeyJson? Plot { get; init; }

    public int? Entries { get; init; }

    public long? Deposit { get; init; }

    public PlotAvailability? Availability { get; init; }
}

internal readonly struct GroupJson
{
    public OwnershipGroupKind? Kind { get; init; }

    public GameRegion? Region { get; init; }

    public uint? HomeWorld { get; init; }

    public ulong? Id { get; init; }
}

internal readonly struct AssignmentJson
{
    public GroupJson? Group { get; init; }

    public PlacementMode? Placement { get; init; }

    public PlotKeyJson[]? Targets { get; init; }

    public long? MaximumDeposit { get; init; }

    public bool? CollectsRefundFirst { get; init; }

    public EligibilityReason? Blockers { get; init; }

    public EligibilityReason? Unresolved { get; init; }

    public DateTime? EvidenceUtc { get; init; }

    public bool? Exact { get; init; }
}

internal readonly struct MemberJson
{
    public ulong? Character { get; init; }

    public string? Name { get; init; }

    public uint? HomeWorld { get; init; }

    public SettingsJson? Settings { get; init; }

    public AssignmentJson? Assignment { get; init; }

    public BatchMemberState? State { get; init; }

    public EntryFailure? Failure { get; init; }

    public EligibilityReason? Reasons { get; init; }

    public PlotKeyJson? Plot { get; init; }

    public long? Deposit { get; init; }

    public DateTime? UpdatedUtc { get; init; }
}
