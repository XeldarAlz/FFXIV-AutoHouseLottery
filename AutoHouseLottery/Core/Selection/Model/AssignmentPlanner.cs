using System.Collections.Immutable;
using System.Runtime.InteropServices;
using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Shared;

namespace AutoHouseLottery.Core.Selection.Model;

internal static class AssignmentPlanner
{
    public static RunPreview Build(RunPolicy policy, ReadOnlySpan<CharacterProfile> roster, IReadOnlyDictionary<PlotKey, PlotSnapshot> plots, DateTime nowUtc)
    {
        var policyError = RunPolicyRules.Validate(policy);
        if (policyError != RunPolicyError.None)
        {
            return Rejected(policy, policyError);
        }

        var ranking = RankTargets(policy, plots);
        var exclusions = ImmutableArray.CreateBuilder<CharacterExclusion>();
        var selected = SelectCharacters(policy, roster, exclusions);
        var planned = PlanCharacters(policy, selected, plots, ranking, exclusions);
        var units = BuildUnits(policy, planned, plots, ranking, exclusions);
        var assignments = Place(policy, planned, units, plots, ranking);
        var odds = new OddsContext(roster, plots, ranking, selected, policy.Cycle.Cycle);
        var groupOdds = EstimateGroupOdds(assignments, odds);
        var blockers = CycleBlockers(policy.Cycle, nowUtc);
        if (assignments.IsEmpty)
        {
            blockers |= RunBlocker.NoAssignment;
        }

        if (!TrySumDeposits(assignments, out var totalMaximumDeposit))
        {
            blockers |= RunBlocker.DepositTotalOverflow;
        }

        if (!groupOdds.IsEmpty && !policy.MultipleWinRiskAccepted)
        {
            blockers |= RunBlocker.MultipleWinsNeedConfirmation;
        }

        return new RunPreview
        {
            PolicyRevision = policy.Revision,
            PolicyError = RunPolicyError.None,
            PlotCount = ranking.Length,
            SelectedCharacterCount = policy.Characters.Length,
            Ranking = ranking,
            Assignments = assignments,
            Exclusions = exclusions.ToImmutable(),
            TotalMaximumDeposit = totalMaximumDeposit,
            Odds = EstimateOdds(assignments, odds, null),
            GroupOdds = groupOdds,
            Blockers = blockers,
        };
    }

    public static ImmutableArray<RunAssignment> FollowConfirmedEntry(ImmutableArray<RunAssignment> assignments, ulong contentId, PlotKey confirmedPlot)
    {
        var leadIndex = IndexOf(assignments, contentId);
        if (leadIndex < 0)
        {
            throw new ArgumentException("The confirmed character has no assignment in this run.", nameof(contentId));
        }

        var lead = assignments[leadIndex];
        if (lead.TargetOrder.IndexOf(confirmedPlot) < 0)
        {
            throw new ArgumentException("The confirmed plot is not one of the lead's planned targets.", nameof(confirmedPlot));
        }

        if (lead.Placement != PlacementMode.SamePlot)
        {
            return assignments;
        }

        ImmutableArray<PlotKey> pinned = [confirmedPlot];
        var followed = assignments.ToBuilder();
        for (var index = 0; index < followed.Count; index++)
        {
            var member = followed[index];
            if (index != leadIndex && member.Group == lead.Group && member.Placement == PlacementMode.SamePlot)
            {
                followed[index] = member with { TargetOrder = pinned };
            }
        }

        return followed.ToImmutable();
    }

    private static RunBlocker CycleBlockers(in CyclePosition cycle, DateTime nowUtc)
    {
        var blockers = RunBlocker.None;
        if (!cycle.IsVerified)
        {
            blockers |= RunBlocker.CycleNotVerified;
        }

        if (cycle.Phase != LotteryPhase.Entry)
        {
            blockers |= RunBlocker.EntryPeriodClosed;
        }

        if (nowUtc < cycle.PhaseStartUtc || nowUtc >= cycle.PhaseEndUtc)
        {
            blockers |= RunBlocker.CycleExpired;
        }

        return blockers;
    }

    private static ImmutableArray<RankedPlot> RankTargets(RunPolicy policy, IReadOnlyDictionary<PlotKey, PlotSnapshot> plots)
    {
        if (policy.Exact is { } exact)
        {
            plots.TryGetValue(exact.Plot, out var snapshot);
            var availability = snapshot?.AvailabilityAt(policy.Cycle) ?? PlotAvailability.Unknown;
            return [new RankedPlot(exact.Plot, snapshot?.EntryCountIn(policy.Cycle.Cycle), exact.Deposit) { Availability = availability }];
        }

        return BestMatchRanking.Rank(policy.Shortlist.GetValueOrDefault().Plots.AsSpan(), plots, policy.Cycle);
    }

    private static List<SelectedCharacter> SelectCharacters(
        RunPolicy policy,
        ReadOnlySpan<CharacterProfile> roster,
        ImmutableArray<CharacterExclusion>.Builder exclusions)
    {
        var profiles = new List<CharacterProfile>(policy.Characters.Length);
        var settings = new List<CharacterRunSettings>(policy.Characters.Length);
        var buyers = new List<BuyerType>(policy.Characters.Length);
        for (var characterIndex = 0; characterIndex < policy.Characters.Length; characterIndex++)
        {
            var characterSettings = policy.Characters[characterIndex];
            if (!TryFindProfile(roster, characterSettings.ContentId, out var profile))
            {
                exclusions.Add(new CharacterExclusion(characterSettings.ContentId, EligibilityReason.ProfileUnavailable));
                continue;
            }

            profiles.Add(profile);
            settings.Add(characterSettings);
            buyers.Add(characterSettings.Buyer);
        }

        var groups = OwnershipGroups.Assign(CollectionsMarshal.AsSpan(profiles), CollectionsMarshal.AsSpan(buyers));
        var selected = new List<SelectedCharacter>(profiles.Count);
        for (var index = 0; index < profiles.Count; index++)
        {
            selected.Add(new SelectedCharacter(settings[index], profiles[index], groups[index]));
        }

        return selected;
    }

    private static List<PlannedCharacter> PlanCharacters(
        RunPolicy policy,
        List<SelectedCharacter> selected,
        IReadOnlyDictionary<PlotKey, PlotSnapshot> plots,
        ImmutableArray<RankedPlot> ranking,
        ImmutableArray<CharacterExclusion>.Builder exclusions)
    {
        var planned = new List<PlannedCharacter>(selected.Count);
        for (var characterIndex = 0; characterIndex < selected.Count; characterIndex++)
        {
            var (settings, profile, group) = selected[characterIndex];
            var characterResult = EligibilityRules.EvaluateCharacter(profile, settings.Buyer, policy.Cycle.Cycle, policy.CollectRefundsFirst);
            if (characterResult.Blockers != EligibilityReason.None)
            {
                exclusions.Add(new CharacterExclusion(settings.ContentId, characterResult.Blockers));
                continue;
            }

            var character = new PlannedCharacter(settings, profile, group, characterResult);
            var removed = EligibilityReason.None;
            for (var plotIndex = 0; plotIndex < ranking.Length; plotIndex++)
            {
                var fit = EvaluatePlot(policy, character, ranking[plotIndex], plots);
                if (fit.Blockers != EligibilityReason.None)
                {
                    removed |= fit.Blockers;
                    continue;
                }

                character.Candidates.Add(plotIndex);
            }

            if (character.Candidates.Count == 0)
            {
                exclusions.Add(new CharacterExclusion(settings.ContentId, removed));
                continue;
            }

            planned.Add(character);
        }

        return planned;
    }

    private static List<PlacementUnit> BuildUnits(
        RunPolicy policy,
        List<PlannedCharacter> planned,
        IReadOnlyDictionary<PlotKey, PlotSnapshot> plots,
        ImmutableArray<RankedPlot> ranking,
        ImmutableArray<CharacterExclusion>.Builder exclusions)
    {
        var units = new List<PlacementUnit>();
        if (policy.Placement == PlacementChoice.Spread)
        {
            for (var characterIndex = 0; characterIndex < planned.Count; characterIndex++)
            {
                units.Add(new PlacementUnit(PlacementMode.Spread, [characterIndex], planned[characterIndex].Candidates));
            }

            return units;
        }

        for (var characterIndex = 0; characterIndex < planned.Count; characterIndex++)
        {
            var character = planned[characterIndex];
            var unitIndex = FindUnit(units, planned, character.Group);
            if (unitIndex < 0)
            {
                units.Add(new PlacementUnit(PlacementMode.SamePlot, [characterIndex], [.. character.Candidates]));
                continue;
            }

            var unit = units[unitIndex];
            var shared = Intersect(unit.Order, character.Candidates);
            if (shared.Count == 0)
            {
                exclusions.Add(new CharacterExclusion(character.Settings.ContentId, ReasonsAgainst(policy, character, unit.Order, plots, ranking)));
                continue;
            }

            unit.Members.Add(characterIndex);
            units[unitIndex] = unit with { Order = shared };
        }

        return units;
    }

    private static ImmutableArray<RunAssignment> Place(
        RunPolicy policy,
        List<PlannedCharacter> planned,
        List<PlacementUnit> units,
        IReadOnlyDictionary<PlotKey, PlotSnapshot> plots,
        ImmutableArray<RankedPlot> ranking)
    {
        var assignments = ImmutableArray.CreateBuilder<RunAssignment>();
        var usedPrimaries = new HashSet<int>();
        var distinctPrimaries = policy.Placement != PlacementChoice.SamePlot;
        for (var unitIndex = 0; unitIndex < units.Count; unitIndex++)
        {
            var unit = units[unitIndex];
            var primaryPosition = distinctPrimaries ? FirstUnused(unit.Order, usedPrimaries) : 0;
            var primary = unit.Order[primaryPosition];
            usedPrimaries.Add(primary);
            var targetOrder = BuildTargetOrder(unit.Order, primaryPosition, policy.MaxInspections, ranking);
            for (var memberIndex = 0; memberIndex < unit.Members.Count; memberIndex++)
            {
                var character = planned[unit.Members[memberIndex]];
                var eligibility = character.CharacterResult.Combine(EvaluatePlot(policy, character, ranking[primary], plots));
                assignments.Add(new RunAssignment(
                    character.Settings.ContentId,
                    character.Group,
                    unit.Mode,
                    targetOrder,
                    MaximumDeposit(targetOrder, ranking, character.Settings.DepositCap),
                    character.Profile.Outstanding == OutstandingSettlement.RefundDue,
                    eligibility));
            }
        }

        return assignments.ToImmutable();
    }

    private static int FirstUnused(List<int> order, HashSet<int> usedPrimaries)
    {
        for (var position = 0; position < order.Count; position++)
        {
            if (!usedPrimaries.Contains(order[position]))
            {
                return position;
            }
        }

        return 0;
    }

    private static ImmutableArray<PlotKey> BuildTargetOrder(List<int> order, int primaryPosition, int maxInspections, ImmutableArray<RankedPlot> ranking)
    {
        var length = Math.Min(order.Count, maxInspections);
        var targets = ImmutableArray.CreateBuilder<PlotKey>(length);
        targets.Add(ranking[order[primaryPosition]].Key);
        for (var position = 0; position < order.Count && targets.Count < length; position++)
        {
            if (position != primaryPosition)
            {
                targets.Add(ranking[order[position]].Key);
            }
        }

        return targets.MoveToImmutable();
    }

    private static long MaximumDeposit(ImmutableArray<PlotKey> targetOrder, ImmutableArray<RankedPlot> ranking, long depositCap)
    {
        var maximum = 0L;
        for (var targetIndex = 0; targetIndex < targetOrder.Length; targetIndex++)
        {
            var deposit = Ranked(ranking, targetOrder[targetIndex]).Deposit;
            if (deposit is not { } known)
            {
                return depositCap;
            }

            maximum = Math.Max(maximum, known);
        }

        return maximum;
    }

    private static RankedPlot Ranked(ImmutableArray<RankedPlot> ranking, PlotKey key)
    {
        for (var index = 0; index < ranking.Length; index++)
        {
            if (ranking[index].Key == key)
            {
                return ranking[index];
            }
        }

        throw new ArgumentException("A planned target is missing from the ranking.", nameof(key));
    }

    private static bool TrySumDeposits(ImmutableArray<RunAssignment> assignments, out long total)
    {
        total = 0;
        for (var index = 0; index < assignments.Length; index++)
        {
            if (!Gil.TryAdd(total, assignments[index].MaximumDeposit, out total))
            {
                return false;
            }
        }

        return true;
    }

    private static OddsEstimate? EstimateOdds(ImmutableArray<RunAssignment> assignments, in OddsContext context, OwnershipGroup? group)
    {
        var draws = new List<PlotDraw>();
        var drawKeys = new List<PlotKey>();
        for (var index = 0; index < assignments.Length; index++)
        {
            var assignment = assignments[index];
            if (group is { } only && assignment.Group != only)
            {
                continue;
            }

            var primary = assignment.TargetOrder[0];
            var drawIndex = drawKeys.IndexOf(primary);
            if (drawIndex < 0)
            {
                drawKeys.Add(primary);
                draws.Add(new PlotDraw(Ranked(context.Ranking, primary).ReportedEntries, 0, 1));
                continue;
            }

            draws[drawIndex] = draws[drawIndex] with { RunEntries = draws[drawIndex].RunEntries + 1 };
        }

        if (draws.Count == 0)
        {
            return null;
        }

        for (var drawIndex = 0; drawIndex < draws.Count; drawIndex++)
        {
            if (!TryCountOwnEntries(context, drawKeys[drawIndex], group, out var ownEntries, out var includedInReport))
            {
                return null;
            }

            var draw = draws[drawIndex];
            draws[drawIndex] = draw with { OwnEntriesInReport = includedInReport, RunEntries = draw.RunEntries + ownEntries };
        }

        return LotteryOdds.TryEstimate(CollectionsMarshal.AsSpan(draws), out var estimate) ? estimate : null;
    }

    private static bool TryCountOwnEntries(in OddsContext context, PlotKey plot, OwnershipGroup? group, out int ownEntries, out int includedInReport)
    {
        ownEntries = 0;
        includedInReport = 0;
        context.Plots.TryGetValue(plot, out var snapshot);
        var countedUtc = snapshot?.EntryCountIn(context.Cycle) is not null ? snapshot.EntryCount?.Provenance.SourceTimeUtc : null;
        for (var index = 0; index < context.Roster.Length; index++)
        {
            var character = context.Roster[index];
            var participation = character.Participation;
            if (participation.StateIn(context.Cycle) != EntrySlotState.Entered || participation.EnteredPlot != plot)
            {
                continue;
            }

            if (group is { } only && !BelongsTo(context.Selected, character.ContentId, only))
            {
                continue;
            }

            if (participation.EnteredUtc is not { } enteredUtc || countedUtc is not { } counted)
            {
                return false;
            }

            ownEntries++;
            if (enteredUtc <= counted)
            {
                includedInReport++;
            }
        }

        return true;
    }

    private static bool BelongsTo(List<SelectedCharacter> selected, ulong contentId, OwnershipGroup group)
    {
        for (var index = 0; index < selected.Count; index++)
        {
            if (selected[index].Settings.ContentId == contentId)
            {
                return selected[index].Group == group;
            }
        }

        return false;
    }

    private static ImmutableArray<GroupOddsEstimate> EstimateGroupOdds(ImmutableArray<RunAssignment> assignments, in OddsContext context)
    {
        var estimates = ImmutableArray.CreateBuilder<GroupOddsEstimate>();
        for (var index = 0; index < assignments.Length; index++)
        {
            var group = assignments[index].Group;
            if (!IsFirstOfGroup(assignments, index) || !SpansSeveralPlots(assignments, group))
            {
                continue;
            }

            estimates.Add(new GroupOddsEstimate(group, EstimateOdds(assignments, context, group)));
        }

        return estimates.ToImmutable();
    }

    private static bool IsFirstOfGroup(ImmutableArray<RunAssignment> assignments, int index)
    {
        for (var earlierIndex = 0; earlierIndex < index; earlierIndex++)
        {
            if (assignments[earlierIndex].Group == assignments[index].Group)
            {
                return false;
            }
        }

        return true;
    }

    private static bool SpansSeveralPlots(ImmutableArray<RunAssignment> assignments, OwnershipGroup group)
    {
        PlotKey? firstPrimary = null;
        for (var index = 0; index < assignments.Length; index++)
        {
            if (assignments[index].Group != group)
            {
                continue;
            }

            var primary = assignments[index].TargetOrder[0];
            if (firstPrimary is not { } seen)
            {
                firstPrimary = primary;
                continue;
            }

            if (seen != primary)
            {
                return true;
            }
        }

        return false;
    }

    private static EligibilityResult EvaluatePlot(RunPolicy policy, PlannedCharacter character, in RankedPlot plot, IReadOnlyDictionary<PlotKey, PlotSnapshot> plots)
    {
        plots.TryGetValue(plot.Key, out var snapshot);
        var candidate = new EntryCandidate(plot.Key, snapshot?.Buyers?.Value ?? PlotBuyers.Unknown, plot.Availability, plot.Deposit)
        {
            NeedsVerification = snapshot?.NeedsVerification ?? false,
        };
        var budget = new CharacterBudget(character.Settings.DepositCap, character.Settings.GilReserve, policy.TravelAllowance);
        return EligibilityRules.EvaluatePlot(character.Profile, character.Settings.Buyer, candidate, budget, default);
    }

    private static EligibilityReason ReasonsAgainst(
        RunPolicy policy,
        PlannedCharacter character,
        List<int> order,
        IReadOnlyDictionary<PlotKey, PlotSnapshot> plots,
        ImmutableArray<RankedPlot> ranking)
    {
        var reasons = EligibilityReason.None;
        for (var position = 0; position < order.Count; position++)
        {
            reasons |= EvaluatePlot(policy, character, ranking[order[position]], plots).Blockers;
        }

        return reasons;
    }

    private static List<int> Intersect(List<int> order, List<int> candidates)
    {
        var shared = new List<int>(order.Count);
        for (var position = 0; position < order.Count; position++)
        {
            if (candidates.Contains(order[position]))
            {
                shared.Add(order[position]);
            }
        }

        return shared;
    }

    private static int FindUnit(List<PlacementUnit> units, List<PlannedCharacter> planned, OwnershipGroup group)
    {
        for (var unitIndex = 0; unitIndex < units.Count; unitIndex++)
        {
            if (planned[units[unitIndex].Members[0]].Group == group)
            {
                return unitIndex;
            }
        }

        return -1;
    }

    private static int IndexOf(ImmutableArray<RunAssignment> assignments, ulong contentId)
    {
        for (var index = 0; index < assignments.Length; index++)
        {
            if (assignments[index].ContentId == contentId)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool TryFindProfile(ReadOnlySpan<CharacterProfile> roster, ulong contentId, out CharacterProfile profile)
    {
        for (var index = 0; index < roster.Length; index++)
        {
            if (roster[index].ContentId == contentId)
            {
                profile = roster[index];
                return true;
            }
        }

        profile = default;
        return false;
    }

    private static RunPreview Rejected(RunPolicy policy, RunPolicyError error) => new()
    {
        PolicyRevision = policy.Revision,
        PolicyError = error,
        PlotCount = 0,
        SelectedCharacterCount = policy.Characters.IsDefault ? 0 : policy.Characters.Length,
        Ranking = [],
        Assignments = [],
        Exclusions = [],
        TotalMaximumDeposit = 0,
        Odds = null,
        GroupOdds = [],
        Blockers = RunBlocker.InvalidPolicy,
    };

    private readonly record struct SelectedCharacter(CharacterRunSettings Settings, CharacterProfile Profile, OwnershipGroup Group);

    private readonly ref struct OddsContext(
        ReadOnlySpan<CharacterProfile> roster,
        IReadOnlyDictionary<PlotKey, PlotSnapshot> plots,
        ImmutableArray<RankedPlot> ranking,
        List<SelectedCharacter> selected,
        CycleKey cycle)
    {
        public ReadOnlySpan<CharacterProfile> Roster { get; } = roster;

        public IReadOnlyDictionary<PlotKey, PlotSnapshot> Plots { get; } = plots;

        public ImmutableArray<RankedPlot> Ranking { get; } = ranking;

        public List<SelectedCharacter> Selected { get; } = selected;

        public CycleKey Cycle { get; } = cycle;
    }

    private sealed class PlannedCharacter(CharacterRunSettings settings, CharacterProfile profile, OwnershipGroup group, EligibilityResult characterResult)
    {
        public CharacterRunSettings Settings { get; } = settings;

        public CharacterProfile Profile { get; } = profile;

        public OwnershipGroup Group { get; } = group;

        public EligibilityResult CharacterResult { get; } = characterResult;

        public List<int> Candidates { get; } = [];
    }

    private readonly record struct PlacementUnit(PlacementMode Mode, List<int> Members, List<int> Order);
}
