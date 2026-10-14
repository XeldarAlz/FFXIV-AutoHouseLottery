using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Find;
using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Localization;
using AutoHouseLottery.Core.Selection.Model;
using System.Numerics;

namespace AutoHouseLottery.Windows.Find;

internal readonly record struct DetailField(string Label, string Value, string? Age);

// One plot's details, formatted and its eligibility evaluated once per plot, view, minute, language, query and character
// rather than on every frame.
internal sealed class DetailsText
{
    private (PlotKey Key, int View, long Minute, LanguageInfo? Language, bool Named, int Query, CharacterProfile? Character, long Reserve) builtFor;

    public string Address { get; private set; } = string.Empty;

    public string World { get; private set; } = string.Empty;

    public PlotStatus? Status { get; private set; }

    public string StatusLine { get; private set; } = string.Empty;

    public DetailField[] Fields { get; private set; } = [];

    public bool Conflicted { get; private set; }

    public bool Listed { get; private set; }

    public string? EnterTitle { get; private set; }

    // The frozen exact-plot policy Enter starts: this plot, for exactly the deposit shown. Null with EnterMissing saying
    // why when it cannot be formed yet.
    public RunPolicy? EnterPolicy { get; private set; }

    public string? EnterMissing { get; private set; }

    public string ForCharacter { get; private set; } = string.Empty;

    public EligibilityText? Eligibility { get; private set; }

    public string? Blockers { get; private set; }

    public string[] Sources { get; private set; } = [];

    public void Refresh(PlotKey key, FindView view, FindState state, Discovery discovery, DateTime nowUtc)
    {
        var character = discovery.Character;
        var inputs = (key, view.Revision, nowUtc.Ticks / TimeSpan.TicksPerMinute, Loc.Current, discovery.Names.Ready, state.QueryRevision, character, state.GilReserve);
        if (inputs == builtFor)
        {
            return;
        }

        builtFor = inputs;
        Address = PlotLabels.Address(discovery.Names, key);
        World = discovery.Names.World(key.WorldId);
        var plot = view.Plot(key);
        Listed = plot is not null;
        Status = plot is null ? null : PlotStatuses.Classify(plot, view.Position);
        StatusLine = StatusLineOf(plot, Status, nowUtc);
        Fields = plot is null ? [] : FieldsOf(plot, view.Position, nowUtc);
        Conflicted = plot?.NeedsVerification ?? false;
        EnterTitle = Status == PlotStatus.EntryOpen && plot?.Price?.Value is { } price ? Loc.T(L.Find.EnterFor, Formatting.Gil(price)) : null;
        (EnterPolicy, EnterMissing) = EnterPolicyOf(plot, key, view, state, character);
        Sources = plot is null ? [] : SourcesOf(plot, nowUtc);
        Eligibility = null;
        Blockers = null;
        ForCharacter = character is { } named ? Loc.T(L.Find.ForCharacter, named.Name) : string.Empty;
        if (character is { } profile && state.Filter.Buyer != BuyerType.Unknown)
        {
            var result = Evaluate(profile, plot, key, view, state);
            Eligibility = EligibilityText.From(result);
            Blockers = result.Blockers == EligibilityReason.None ? null : FindText.Reasons(result.Blockers);
        }
    }

    private static (RunPolicy? Policy, string? Missing) EnterPolicyOf(PlotSnapshot? plot, PlotKey key, FindView view, FindState state, CharacterProfile? character)
    {
        if (plot?.Price?.Value is not { } deposit || PlotStatuses.Classify(plot, view.Position) != PlotStatus.EntryOpen)
        {
            return (null, null);
        }

        if (character is not { } profile)
        {
            return (null, Loc.T(L.Find.NeedCharacter));
        }

        var buyer = RunPreviewBuilder.BuyerFor(plot.Buyers?.Value, state.Filter.Buyer);
        return buyer == BuyerType.Unknown
            ? (null, Loc.T(L.Find.NeedBuyer))
            : (RunPreviewBuilder.ExactPolicy(profile, buyer, key, deposit, state.GilReserve, view.Position, view.Revision), null);
    }

    // Without a price ceiling the cap would refuse every plot, so this view shows the other checks; the run preview keeps
    // the real cap.
    private static EligibilityResult Evaluate(in CharacterProfile character, PlotSnapshot? plot, PlotKey key, FindView view, FindState state)
    {
        var filter = state.Filter;
        var candidate = new EntryCandidate(key, plot?.Buyers?.Value ?? PlotBuyers.Unknown, plot?.AvailabilityAt(view.Position) ?? PlotAvailability.Unknown, plot?.Price?.Value)
        {
            NeedsVerification = plot?.NeedsVerification ?? false,
        };
        var budget = new CharacterBudget(filter.PriceCeiling > 0 ? filter.PriceCeiling : long.MaxValue, state.GilReserve, RunPreviewBuilder.PreviewTravelAllowance);
        return EligibilityRules.EvaluatePlot(character, filter.Buyer, candidate, budget, default)
            .Combine(EligibilityRules.EvaluateCharacter(character, filter.Buyer, view.Position.Cycle, true));
    }

    private static string StatusLineOf(PlotSnapshot? plot, PlotStatus? status, DateTime nowUtc)
    {
        var label = FindText.Status(status);
        if (plot?.Sale?.Value.PhaseEndUtc is { } end && status is PlotStatus.EntryOpen or PlotStatus.Results or PlotStatus.NextCycle)
        {
            return string.Concat(label, TextDraw.Separator, Loc.T(L.Find.PhaseEnds, FindText.LocalTime(end), FindText.Countdown(end - nowUtc)));
        }

        return label;
    }

    private static DetailField[] FieldsOf(PlotSnapshot plot, in CyclePosition position, DateTime nowUtc)
    {
        var entries = plot.EntryCountIn(position.Cycle);
        return
        [
            Field(Loc.T(L.Find.FieldSize), FindText.Size(plot.Size?.Value), plot.Size?.Provenance, nowUtc),
            Field(Loc.T(L.Find.FieldDeposit), FindText.Gil(plot.Price?.Value), plot.Price?.Provenance, nowUtc),
            Field(Loc.T(L.Find.FieldBuyers), FindText.Buyers(plot.Buyers?.Value), plot.Buyers?.Provenance, nowUtc),
            Field(Loc.T(L.Find.FieldPhase), FindText.Status(PlotStatuses.Classify(plot, position)), plot.Sale?.Provenance, nowUtc),
            Field(Loc.T(L.Find.FieldEntries), FindText.Count(entries), entries is null ? null : plot.EntryCount?.Provenance, nowUtc),
            Field(Loc.T(L.Find.FieldMap), Loc.T(plot.Key.IsSubdivision ? L.Find.SubdivisionLong : L.Find.MainDivisionLong), null, nowUtc),
        ];
    }

    private static DetailField Field(string label, string value, Provenance? provenance, DateTime nowUtc)
    {
        return new DetailField(label, value, provenance is { } source ? FindText.Observed(source.SourceTimeUtc, nowUtc) : null);
    }

    private static string[] SourcesOf(PlotSnapshot plot, DateTime nowUtc)
    {
        var lines = new List<string>();
        Source(lines, Loc.T(L.Find.FieldSize), plot.Size?.Provenance, nowUtc);
        Source(lines, Loc.T(L.Find.FieldDeposit), plot.Price?.Provenance, nowUtc);
        Source(lines, Loc.T(L.Find.FieldBuyers), plot.Buyers?.Provenance, nowUtc);
        Source(lines, Loc.T(L.Find.FieldPhase), plot.Sale?.Provenance, nowUtc);
        Source(lines, Loc.T(L.Find.FieldEntries), plot.EntryCount?.Provenance, nowUtc);
        return [.. lines];
    }

    private static void Source(List<string> lines, string field, Provenance? provenance, DateTime nowUtc)
    {
        if (provenance is not { } source)
        {
            return;
        }

        var origin = FindText.Source(source.Source);
        if (source.Cycle is { } cycle)
        {
            origin = string.Concat(origin, TextDraw.Separator, Loc.T(L.Find.SourceCycle, cycle.Number));
        }

        lines.Add(Loc.T(L.Find.SourceLine, field, origin, FindText.Age(source.SourceTimeUtc, nowUtc), FindText.Age(source.ReceivedUtc, nowUtc)));
    }

    public Vector4 StatusColor => StatusMarks.Color(Status);
}
