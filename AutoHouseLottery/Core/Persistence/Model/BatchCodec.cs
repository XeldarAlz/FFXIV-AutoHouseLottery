using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.Json;
using AutoHouseLottery.Core.Batch.Model;
using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Selection.Model;
using AutoHouseLottery.Core.Tasks.Model;

namespace AutoHouseLottery.Core.Persistence.Model;

// The batch checkpoint file's format. A file that is not complete in every field the batch needs is refused whole: a
// checkpoint only orders the work, the journal holds what happened, so a refused file loses the batch and nothing else.
internal static class BatchCodec
{
    public const int Schema = 1;

    public static byte[] Serialize(BatchCheckpoint checkpoint)
    {
        var members = new MemberJson[checkpoint.Members.Length];
        for (var index = 0; index < members.Length; index++)
        {
            members[index] = ToJson(checkpoint.Members[index]);
        }

        var ranking = new RankedPlotJson[checkpoint.Ranking.Length];
        for (var index = 0; index < ranking.Length; index++)
        {
            var plot = checkpoint.Ranking[index];
            ranking[index] = new RankedPlotJson { Plot = PlotKeyJson.From(plot.Key), Entries = plot.ReportedEntries, Deposit = plot.Deposit, Availability = plot.Availability };
        }

        var batch = new BatchJson
        {
            Id = checkpoint.BatchId,
            CreatedUtc = checkpoint.CreatedUtc,
            UpdatedUtc = checkpoint.UpdatedUtc,
            DryRun = checkpoint.DryRunRequested,
            Phase = checkpoint.Phase,
            Halt = checkpoint.Halt,
            SwitchTarget = checkpoint.SwitchTarget,
            Policy = ToJson(checkpoint.Policy),
            Ranking = ranking,
            Members = members,
        };
        return JsonSerializer.SerializeToUtf8Bytes(new BatchFileJson { Schema = Schema, Batch = batch }, BatchJsonContext.Default.BatchFileJson);
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> bytes, out BatchCheckpoint checkpoint)
    {
        checkpoint = null!;
        BatchFileJson document;
        try
        {
            document = JsonSerializer.Deserialize(bytes, BatchJsonContext.Default.BatchFileJson);
        }
        catch (JsonException)
        {
            return false;
        }

        if (document.Schema != Schema
            || document.Batch is not { } batch
            || batch.Id is not { } id
            || batch.CreatedUtc is not { } created
            || batch.UpdatedUtc is not { } updated
            || batch.DryRun is not { } dryRun
            || batch.Phase is not { } phase
            || batch.Halt is not { } halt
            || batch.Policy is not { } storedPolicy
            || !TryPolicy(storedPolicy, out var policy)
            || !TryRanking(batch.Ranking, out var ranking)
            || !TryMembers(batch.Members, out var members))
        {
            return false;
        }

        checkpoint = new BatchCheckpoint
        {
            BatchId = id,
            Policy = policy,
            Ranking = ranking,
            DryRunRequested = dryRun,
            Members = members,
            Phase = phase,
            Halt = halt,
            SwitchTarget = batch.SwitchTarget,
            CreatedUtc = created,
            UpdatedUtc = updated,
        };
        return true;
    }

    private static PolicyJson ToJson(RunPolicy policy)
    {
        var characters = new SettingsJson[policy.Characters.Length];
        for (var index = 0; index < characters.Length; index++)
        {
            characters[index] = ToJson(policy.Characters[index]);
        }

        var cycle = policy.Cycle;
        return new PolicyJson
        {
            Revision = policy.Revision,
            Cycle = new CyclePositionJson
            {
                Cycle = CycleKeyJson.From(cycle.Cycle),
                Phase = cycle.Phase,
                StartUtc = cycle.PhaseStartUtc,
                EndUtc = cycle.PhaseEndUtc,
                ScheduleRevision = cycle.ScheduleRevision,
                Evidence = cycle.Evidence,
            },
            Characters = characters,
            TravelAllowance = policy.TravelAllowance,
            Shortlist = policy.Shortlist is { } shortlist ? new ShortlistRefJson { Id = shortlist.Id, Revision = shortlist.Revision, Plots = ToJson(shortlist.Plots) } : null,
            Placement = policy.Placement,
            MultipleWinRiskAccepted = policy.MultipleWinRiskAccepted,
            MaxInspections = policy.MaxInspections,
            CollectRefundsFirst = policy.CollectRefundsFirst,
        };
    }

    private static SettingsJson ToJson(in CharacterRunSettings settings) => new()
    {
        Character = settings.ContentId,
        Buyer = settings.Buyer,
        DepositCap = settings.DepositCap,
        Reserve = settings.GilReserve,
        Target = settings.Target is { } target ? PlotKeyJson.From(target) : null,
    };

    private static MemberJson ToJson(BatchMember member) => new()
    {
        Character = member.ContentId,
        Name = member.Name,
        HomeWorld = member.HomeWorldId,
        Settings = ToJson(member.Settings),
        Assignment = member.Assignment is { } assignment ? ToJson(assignment) : null,
        State = member.State,
        Failure = member.Failure,
        Reasons = member.Reasons,
        Plot = member.Plot is { } plot ? PlotKeyJson.From(plot) : null,
        Deposit = member.Deposit,
        UpdatedUtc = member.UpdatedUtc,
    };

    private static AssignmentJson ToJson(in RunAssignment assignment) => new()
    {
        Group = new GroupJson { Kind = assignment.Group.Kind, Region = assignment.Group.Region, HomeWorld = assignment.Group.HomeWorldId, Id = assignment.Group.Id },
        Placement = assignment.Placement,
        Targets = ToJson(assignment.TargetOrder),
        MaximumDeposit = assignment.MaximumDeposit,
        CollectsRefundFirst = assignment.CollectsRefundFirst,
        Blockers = assignment.Eligibility.Blockers,
        Unresolved = assignment.Eligibility.Unresolved,
        EvidenceUtc = assignment.Eligibility.EvidenceUtc,
        Exact = assignment.Exact,
    };

    private static PlotKeyJson[] ToJson(ImmutableArray<PlotKey> plots)
    {
        var stored = new PlotKeyJson[plots.Length];
        for (var index = 0; index < stored.Length; index++)
        {
            stored[index] = PlotKeyJson.From(plots[index]);
        }

        return stored;
    }

    private static bool TryPolicy(in PolicyJson stored, out RunPolicy policy)
    {
        policy = null!;
        if (stored.Revision is not { } revision
            || stored.Cycle is not { } cycle
            || cycle.Cycle is not { } cycleKey
            || !cycleKey.TryToKey(out var key)
            || cycle.Phase is not { } phase
            || cycle.StartUtc is not { } start
            || cycle.EndUtc is not { } end
            || cycle.ScheduleRevision is not { } scheduleRevision
            || cycle.Evidence is not { } evidence
            || stored.Characters is not { Length: > 0 } characters
            || stored.TravelAllowance is not { } travel
            || stored.Placement is not { } placement
            || stored.MultipleWinRiskAccepted is not { } accepted
            || stored.MaxInspections is not { } inspections
            || stored.CollectRefundsFirst is not { } collectFirst)
        {
            return false;
        }

        var settings = new CharacterRunSettings[characters.Length];
        for (var index = 0; index < settings.Length; index++)
        {
            if (!TrySettings(characters[index], out settings[index]))
            {
                return false;
            }
        }

        ShortlistSnapshot? shortlist = null;
        if (stored.Shortlist is { } storedShortlist)
        {
            if (storedShortlist.Id is not { } shortlistId || storedShortlist.Revision is not { } shortlistRevision || !TryPlots(storedShortlist.Plots, out var plots))
            {
                return false;
            }

            shortlist = new ShortlistSnapshot(shortlistId, shortlistRevision, plots);
        }

        policy = new RunPolicy
        {
            Revision = revision,
            Cycle = new CyclePosition(key, phase, start, end, scheduleRevision, evidence),
            Characters = ImmutableCollectionsMarshal.AsImmutableArray(settings),
            TravelAllowance = travel,
            Shortlist = shortlist,
            Placement = placement,
            MultipleWinRiskAccepted = accepted,
            MaxInspections = inspections,
            CollectRefundsFirst = collectFirst,
        };
        return RunPolicyRules.Validate(policy) == RunPolicyError.None;
    }

    private static bool TrySettings(in SettingsJson stored, out CharacterRunSettings settings)
    {
        settings = default;
        if (stored.Character is not { } contentId || stored.Buyer is not { } buyer || stored.DepositCap is not { } cap || stored.Reserve is not { } reserve)
        {
            return false;
        }

        PlotKey? target = null;
        if (stored.Target is { } storedTarget)
        {
            if (!storedTarget.TryToKey(out var key))
            {
                return false;
            }

            target = key;
        }

        settings = new CharacterRunSettings(contentId, buyer, cap, reserve) { Target = target };
        return true;
    }

    private static bool TryRanking(RankedPlotJson[]? stored, out ImmutableArray<RankedPlot> ranking)
    {
        ranking = [];
        if (stored is null)
        {
            return false;
        }

        var plots = new RankedPlot[stored.Length];
        for (var index = 0; index < plots.Length; index++)
        {
            var plot = stored[index];
            if (plot.Plot is not { } storedKey || !storedKey.TryToKey(out var key) || plot.Availability is not { } availability)
            {
                return false;
            }

            plots[index] = new RankedPlot(key, plot.Entries, plot.Deposit) { Availability = availability };
        }

        ranking = ImmutableCollectionsMarshal.AsImmutableArray(plots);
        return true;
    }

    private static bool TryMembers(MemberJson[]? stored, out ImmutableArray<BatchMember> members)
    {
        members = [];
        if (stored is not { Length: > 0 })
        {
            return false;
        }

        var loaded = new BatchMember[stored.Length];
        for (var index = 0; index < loaded.Length; index++)
        {
            var member = stored[index];
            if (member.Character is not { } contentId
                || member.Name is not { } name
                || member.HomeWorld is not { } homeWorld
                || member.Settings is not { } storedSettings
                || !TrySettings(storedSettings, out var settings)
                || member.State is not { } state
                || member.Failure is not { } failure
                || member.Reasons is not { } reasons
                || member.UpdatedUtc is not { } updated)
            {
                return false;
            }

            RunAssignment? assignment = null;
            if (member.Assignment is { } storedAssignment)
            {
                if (!TryAssignment(storedAssignment, contentId, out var loadedAssignment))
                {
                    return false;
                }

                assignment = loadedAssignment;
            }

            PlotKey? plot = null;
            if (member.Plot is { } storedPlot)
            {
                if (!storedPlot.TryToKey(out var key))
                {
                    return false;
                }

                plot = key;
            }

            loaded[index] = new BatchMember
            {
                ContentId = contentId,
                Name = name,
                HomeWorldId = homeWorld,
                Settings = settings,
                Assignment = assignment,
                State = state,
                Failure = failure,
                Reasons = reasons,
                Plot = plot,
                Deposit = member.Deposit,
                UpdatedUtc = updated,
            };
        }

        members = ImmutableCollectionsMarshal.AsImmutableArray(loaded);
        return true;
    }

    private static bool TryAssignment(in AssignmentJson stored, ulong contentId, out RunAssignment assignment)
    {
        assignment = default;
        if (stored.Group is not { } group
            || group.Kind is not { } kind
            || group.Region is not { } region
            || group.HomeWorld is not { } homeWorld
            || group.Id is not { } groupId
            || stored.Placement is not { } placement
            || !TryPlots(stored.Targets, out var targets)
            || targets.IsEmpty
            || stored.MaximumDeposit is not { } maximum
            || stored.CollectsRefundFirst is not { } collectFirst
            || stored.Blockers is not { } blockers
            || stored.Unresolved is not { } unresolved
            || stored.Exact is not { } exact)
        {
            return false;
        }

        assignment = new RunAssignment(
            contentId,
            new OwnershipGroup(kind, region, homeWorld, groupId),
            placement,
            targets,
            maximum,
            collectFirst,
            new EligibilityResult(blockers, unresolved, stored.EvidenceUtc))
        {
            Exact = exact,
        };
        return true;
    }

    private static bool TryPlots(PlotKeyJson[]? stored, out ImmutableArray<PlotKey> plots)
    {
        plots = [];
        if (stored is null)
        {
            return false;
        }

        var keys = new PlotKey[stored.Length];
        for (var index = 0; index < keys.Length; index++)
        {
            if (!stored[index].TryToKey(out keys[index]))
            {
                return false;
            }
        }

        plots = ImmutableCollectionsMarshal.AsImmutableArray(keys);
        return true;
    }
}
