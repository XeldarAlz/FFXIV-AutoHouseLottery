using System.Collections.Immutable;
using System.Numerics;
using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Find;
using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Core.Selection.Model;

namespace AutoHouseLottery.Windows.Find;

// One character's row on the Characters expander. TargetKeys line up with TargetLabels; its first entry is the best match
// (null). TargetIndex is -1 when the chosen plot has left the shortlist.
internal sealed record CharacterRowText(
    RosterEntry Entry,
    string Title,
    string Status,
    Vector4 StatusColor,
    string[] TargetLabels,
    PlotKey?[] TargetKeys,
    int TargetIndex,
    string? Exclusion);

// One planned entry of the batch preview: who goes where, and what that character must hold.
internal readonly record struct BatchLineText(string Title, string Detail, string? Note);

// The Characters expander's and the batch preview's words, formatted once per view, roster, switching state and language
// rather than per frame.
internal sealed class CharactersText
{
    public const int AccountGroups = 8;

    private const string Separator = " · ";
    private const string Arrow = " → ";
    private const string HeaderId = "##ahl_find_characters";

    private (int View, int Roster, bool Switching, LanguageInfo? Language, bool Named) builtFor = (-1, -1, false, null, false);

    public string HeaderLabel { get; private set; } = string.Empty;

    public ImmutableArray<CharacterRowText> Rows { get; private set; } = [];

    public string[] AccountLabels { get; private set; } = [];

    public ImmutableArray<BatchLineText> Lines { get; private set; } = [];

    public string[] Exclusions { get; private set; } = [];

    public string Counts { get; private set; } = string.Empty;

    public string Total { get; private set; } = string.Empty;

    public string? OddsAny { get; private set; }

    public string? OddsTwoOrMore { get; private set; }

    public string Switching { get; private set; } = string.Empty;

    public string EnterTitle { get; private set; } = string.Empty;

    public void Refresh(FindView view, Discovery discovery, FindState state, bool switchingAllowed)
    {
        var key = (view.Revision, state.RosterRevision, switchingAllowed, Loc.Current, discovery.Names.Ready);
        if (key == builtFor)
        {
            return;
        }

        builtFor = key;
        HeaderLabel = string.Concat(Loc.T(L.Characters.Title), Separator, Loc.Plural(L.Characters.Selected, state.SelectedCount), HeaderId);
        AccountLabels = BuildAccountLabels();
        Rows = BuildRows(view, discovery, state);
        BuildBatch(view, discovery, state, switchingAllowed);
    }

    private static string[] BuildAccountLabels()
    {
        var labels = new string[AccountGroups + 1];
        labels[0] = Loc.T(L.Characters.AccountNone);
        for (var group = 1; group <= AccountGroups; group++)
        {
            labels[group] = Loc.T(L.Characters.AccountNumber, group);
        }

        return labels;
    }

    private static ImmutableArray<CharacterRowText> BuildRows(FindView view, Discovery discovery, FindState state)
    {
        var roster = state.Roster;
        var rows = ImmutableArray.CreateBuilder<CharacterRowText>(roster.Length);
        var loggedIn = discovery.Character?.ContentId;
        var now = DateTime.UtcNow;
        for (var index = 0; index < roster.Length; index++)
        {
            var entry = roster[index];
            var facts = entry.Facts;
            var profile = view.RosterProfile(entry.ContentId);
            var entered = profile is { } known && known.Participation.StateIn(view.Position.Cycle) == EntrySlotState.Entered;
            string status;
            if (entered)
            {
                status = Loc.T(L.Characters.AlreadyEntered);
            }
            else if (entry.ContentId == loggedIn)
            {
                status = Loc.T(L.Characters.LoggedIn, FindText.Gil(discovery.Character?.ObservedGil));
            }
            else
            {
                status = facts.Gil is { } gil
                    ? Loc.T(L.Characters.GilEstimate, Loc.Gil(gil), FindText.Age(facts.GilSeenUtc ?? default, now))
                    : Loc.T(L.Characters.GilUnknown);
            }

            var (labels, keys, selected) = Targets(view, discovery, entry);
            rows.Add(new CharacterRowText(
                entry,
                string.Concat(facts.Name, Separator, discovery.Names.World(facts.HomeWorldId)),
                status,
                entered ? Styling.AccentBlue : entry.ContentId == loggedIn ? Styling.AccentMint : Styling.TextDim,
                labels,
                keys,
                selected,
                ExclusionOf(view.Batch?.Preview, entry.ContentId)));
        }

        return rows.MoveToImmutable();
    }

    // Best match, then every shortlisted plot on the character's own home world, the only world it can enter on.
    private static (string[] Labels, PlotKey?[] Keys, int Selected) Targets(FindView view, Discovery discovery, RosterEntry entry)
    {
        var shortlist = view.Shortlist;
        var labels = new List<string>(shortlist.Length + 1) { Loc.T(L.Characters.BestMatch) };
        var keys = new List<PlotKey?>(shortlist.Length + 1) { null };
        var selected = entry.Selection.Target is null ? 0 : -1;
        for (var index = 0; index < shortlist.Length; index++)
        {
            var plot = shortlist[index].Key;
            if (plot.WorldId != entry.Facts.HomeWorldId)
            {
                continue;
            }

            if (plot == entry.Selection.Target)
            {
                selected = keys.Count;
            }

            labels.Add(PlotLabels.Address(discovery.Names, plot));
            keys.Add(plot);
        }

        return ([.. labels], [.. keys], selected);
    }

    private static string? ExclusionOf(RunPreview? preview, ulong contentId)
    {
        if (preview is null)
        {
            return null;
        }

        for (var index = 0; index < preview.Exclusions.Length; index++)
        {
            if (preview.Exclusions[index].ContentId == contentId)
            {
                return FindText.Reasons(preview.Exclusions[index].Reasons);
            }
        }

        return null;
    }

    private void BuildBatch(FindView view, Discovery discovery, FindState state, bool switchingAllowed)
    {
        if (view.Batch is not { } batch)
        {
            Lines = [];
            Exclusions = [];
            OddsAny = null;
            OddsTwoOrMore = null;
            return;
        }

        var preview = batch.Preview;
        Counts = Loc.T(L.Find.CountsJoin, Loc.Plural(L.Find.PlotCount, state.Shortlist.Count), Loc.Plural(L.Find.CharacterCount, preview.SelectedCharacterCount));
        var lines = ImmutableArray.CreateBuilder<BatchLineText>(preview.Assignments.Length);
        for (var index = 0; index < preview.Assignments.Length; index++)
        {
            lines.Add(Line(preview, batch, discovery, preview.Assignments[index]));
        }

        Lines = lines.MoveToImmutable();
        var exclusions = new string[preview.Exclusions.Length];
        for (var index = 0; index < exclusions.Length; index++)
        {
            var exclusion = preview.Exclusions[index];
            exclusions[index] = Loc.T(L.Find.Excluded, NameOf(state, exclusion.ContentId), FindText.Reasons(exclusion.Reasons));
        }

        Exclusions = exclusions;
        Total = Loc.Gil(preview.TotalMaximumDeposit);
        OddsAny = preview.Odds is { } odds ? Loc.T(L.Batch.OddsAny, odds.AnyWin.ToString("P0", Loc.Culture)) : null;
        OddsTwoOrMore = TwoOrMore(preview.GroupOdds) is { } twoOrMore ? Loc.T(L.Batch.OddsTwoOrMore, twoOrMore.ToString("P0", Loc.Culture)) : null;
        Switching = Loc.T(switchingAllowed ? L.Batch.SwitchingOn : L.Batch.SwitchingOff);
        EnterTitle = Loc.Plural(L.Batch.Enter, preview.Assignments.Length);
    }

    // "Name → Mist · Ward 12 · Plot 5", then the fallbacks, the most it may pay, what it must hold and what it has.
    private static BatchLineText Line(RunPreview preview, BatchView batch, Discovery discovery, in RunAssignment assignment)
    {
        var profile = ProfileOf(batch.Profiles, assignment.ContentId);
        var settings = SettingsOf(batch.Policy, assignment.ContentId);
        var title = string.Concat(profile?.Name ?? string.Empty, Arrow, PlotLabels.Address(discovery.Names, assignment.TargetOrder[0]));
        var route = assignment.Exact ? Loc.T(L.Batch.Exact) : assignment.TargetOrder.Length > 1 ? Loc.Plural(L.Batch.Fallbacks, assignment.TargetOrder.Length - 1) : null;
        var detail = string.Concat(route is null ? string.Empty : string.Concat(route, Separator), Loc.T(L.Batch.UpTo, Loc.Gil(assignment.MaximumDeposit)));
        if (settings is { } own && BudgetRules.TryRequired(new CharacterBudget(own.DepositCap, own.GilReserve, 0), assignment.MaximumDeposit, out var required))
        {
            detail = string.Concat(detail, Separator, Loc.T(L.Batch.Needs, Loc.Gil(required)));
        }

        if (profile is { ObservedGil: { } gil } known)
        {
            detail = string.Concat(detail, Separator, Loc.T(known.GilIsEstimate ? L.Batch.HasEstimate : L.Batch.Has, Loc.Gil(gil)));
        }

        return new BatchLineText(title, detail, assignment.CollectsRefundFirst ? Loc.T(L.Batch.RefundFirst) : null);
    }

    private static double? TwoOrMore(ImmutableArray<GroupOddsEstimate> groups)
    {
        double? highest = null;
        for (var index = 0; index < groups.Length; index++)
        {
            if (groups[index].Estimate is not { } estimate)
            {
                return null;
            }

            highest = Math.Max(highest ?? 0d, estimate.TwoOrMoreWins);
        }

        return highest;
    }

    private static CharacterProfile? ProfileOf(ImmutableArray<CharacterProfile> profiles, ulong contentId)
    {
        for (var index = 0; index < profiles.Length; index++)
        {
            if (profiles[index].ContentId == contentId)
            {
                return profiles[index];
            }
        }

        return null;
    }

    private static CharacterRunSettings? SettingsOf(RunPolicy? policy, ulong contentId)
    {
        if (policy is null)
        {
            return null;
        }

        for (var index = 0; index < policy.Characters.Length; index++)
        {
            if (policy.Characters[index].ContentId == contentId)
            {
                return policy.Characters[index];
            }
        }

        return null;
    }

    private static string NameOf(FindState state, ulong contentId)
    {
        var roster = state.Roster;
        for (var index = 0; index < roster.Length; index++)
        {
            if (roster[index].ContentId == contentId)
            {
                return roster[index].Facts.Name;
            }
        }

        return Loc.T(L.Lottery.UnknownCharacter);
    }
}
