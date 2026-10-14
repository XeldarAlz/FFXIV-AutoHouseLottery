using System.Collections.Immutable;
using AutoHouseLottery.Core.Eligibility.Model;
using AutoHouseLottery.Core.Find.Model;
using AutoHouseLottery.Core.Housing.Model;
using AutoHouseLottery.Core.Providers.Model;
using AutoHouseLottery.Core.Selection.Model;

namespace AutoHouseLottery.Core.Find;

// Everything the Find a home page draws, computed together on a task from one snapshot so the map, the list, the
// shortlist drawer and the preview always describe the same data. The revision changes with every rebuild, so labels
// drawn from a view can be cached until it changes.
internal sealed class FindView
{
    public static readonly FindView Empty = new()
    {
        Revision = 0,
        Results = FindResults.Empty,
        Plots = DiscoverySnapshot.Empty.Plots,
        Position = default,
        AreaCovered = false,
        Shortlist = [],
        ShortlistRevision = -1,
        QueryRevision = -1,
        BestMatch = [],
        Preview = null,
        PreviewPolicy = null,
        UnavailableCount = 0,
        Batch = null,
        RosterProfiles = [],
    };

    public required int Revision { get; init; }

    public required FindResults Results { get; init; }

    public required IReadOnlyDictionary<PlotKey, PlotSnapshot> Plots { get; init; }

    public required CyclePosition Position { get; init; }

    public required bool AreaCovered { get; init; }

    public required ImmutableArray<ShortlistRow> Shortlist { get; init; }

    public required int ShortlistRevision { get; init; }

    public required int QueryRevision { get; init; }

    public required ImmutableArray<RankedPlot> BestMatch { get; init; }

    public required RunPreview? Preview { get; init; }

    // The frozen policy the preview was planned from, which Enter best match starts; null when it could not be planned.
    public required RunPolicy? PreviewPolicy { get; init; }

    public required int UnavailableCount { get; init; }

    // The batch plan, when the Characters expander selects more than the logged-in character; null otherwise.
    public required BatchView? Batch { get; init; }

    // Every known character's profile, in the roster's order when the view was built.
    public required ImmutableArray<CharacterProfile> RosterProfiles { get; init; }

    public CharacterProfile? RosterProfile(ulong contentId)
    {
        for (var index = 0; index < RosterProfiles.Length; index++)
        {
            if (RosterProfiles[index].ContentId == contentId)
            {
                return RosterProfiles[index];
            }
        }

        return null;
    }

    public PlotSnapshot? Plot(PlotKey key) => Plots.TryGetValue(key, out var plot) ? plot : null;

    public PlotStatus? StatusOf(PlotKey key) => Plots.TryGetValue(key, out var plot) ? PlotStatuses.Classify(plot, Position) : null;
}

// The batch preview of UX section 8: the plan for every selected character, the profiles it was planned from (offline
// characters with their last login's gil as an estimate), and the frozen policy Enter for N characters starts. Revision is
// the roster revision the plan was built from, so Enter waits until it describes what is on screen.
internal sealed record BatchView(
    RunPreview Preview,
    RunPolicy? Policy,
    ImmutableArray<CharacterProfile> Profiles,
    int RosterRevision);
