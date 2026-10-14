using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Find;
using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Core.Selection.Model;
using System.Numerics;

namespace AutoHouseLottery.Windows.Find;

internal readonly record struct EligibilityText(string Verdict, Vector4 Color, string? Unresolved)
{
    public static EligibilityText From(in EligibilityResult result) => result.Verdict switch
    {
        EligibilityVerdict.Eligible => new EligibilityText(Loc.T(L.Find.EligibilityEligible), Styling.AccentMint, null),
        EligibilityVerdict.Ineligible => new EligibilityText(Loc.T(L.Find.EligibilityIneligible), Styling.AccentRose, null),
        _ => new EligibilityText(
            Loc.T(L.Find.EligibilityUnknown),
            Styling.AccentAmberSoft,
            result.Unresolved == EligibilityReason.None ? null : Loc.T(L.Find.Unresolved, FindText.Reasons(result.Unresolved))),
    };
}

// The shortlist drawer's words, formatted once per view, character, language and shortlist size rather than per frame.
internal sealed class DrawerText
{
    private (int View, ulong Character, int Count, LanguageInfo? Language, bool Named) builtFor = (-1, 0, -1, null, false);

    public string Counts { get; private set; } = string.Empty;

    public string Character { get; private set; } = string.Empty;

    public string BestDetail { get; private set; } = string.Empty;

    public string MaximumDeposit { get; private set; } = string.Empty;

    public string[] Exclusions { get; private set; } = [];

    public EligibilityText Eligibility { get; private set; }

    public void Refresh(FindView view, Discovery discovery, int shortlistCount)
    {
        var character = discovery.Character;
        var key = (view.Revision, character?.ContentId ?? 0, shortlistCount, Loc.Current, discovery.Names.Ready);
        if (key == builtFor)
        {
            return;
        }

        builtFor = key;
        Counts = Loc.T(L.Find.CountsJoin, Loc.Plural(L.Find.PlotCount, shortlistCount), Loc.Plural(L.Find.CharacterCount, character is null ? 0 : 1));
        Character = character is { } profile ? string.Concat(profile.Name, TextDraw.Separator, discovery.Names.World(profile.HomeWorldId)) : string.Empty;
        BestDetail = view.BestMatch.IsEmpty
            ? string.Empty
            : string.Concat(Loc.T(L.Find.Deposit, FindText.Gil(view.BestMatch[0].Deposit)), TextDraw.Separator, Loc.T(L.Find.EntriesValue, FindText.Count(view.BestMatch[0].ReportedEntries)));
        var preview = view.Preview;
        var exclusions = preview?.Exclusions ?? [];
        var lines = new string[exclusions.Length];
        for (var index = 0; index < lines.Length; index++)
        {
            lines[index] = Loc.T(L.Find.Excluded, character?.Name ?? string.Empty, FindText.Reasons(exclusions[index].Reasons));
        }

        Exclusions = lines;
        if (preview is { PolicyError: RunPolicyError.None, Assignments.Length: > 0 } planned)
        {
            MaximumDeposit = FindText.Gil(planned.Assignments[0].MaximumDeposit);
            Eligibility = EligibilityText.From(planned.Assignments[0].Eligibility);
        }
    }
}
