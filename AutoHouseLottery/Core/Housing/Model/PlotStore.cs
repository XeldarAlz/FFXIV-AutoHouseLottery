using System.Collections.Frozen;
using System.Threading;

namespace AutoHouseLottery.Core.Housing.Model;

internal sealed class PlotStore
{
    private readonly Dictionary<PlotKey, PlotSnapshot> plots = [];
    private readonly Dictionary<(CoverageScope Scope, ObservationSource Source), CoverageSnapshot> coverage = [];
    private FrozenDictionary<PlotKey, PlotSnapshot> published = FrozenDictionary<PlotKey, PlotSnapshot>.Empty;
    private FrozenDictionary<(CoverageScope Scope, ObservationSource Source), CoverageSnapshot> publishedCoverage =
        FrozenDictionary<(CoverageScope Scope, ObservationSource Source), CoverageSnapshot>.Empty;
    private int revision;

    public IReadOnlyDictionary<PlotKey, PlotSnapshot> Plots => Volatile.Read(ref published);

    public int Revision => Volatile.Read(ref revision);

    public bool TryGetCoverage(in CoverageScope scope, ObservationSource source, out CoverageSnapshot snapshot)
    {
        return Volatile.Read(ref publishedCoverage).TryGetValue((scope, source), out snapshot);
    }

    public MergeRejection Merge(in PlotObservation observation)
    {
        var rejection = MergeRow(observation);
        if (rejection == MergeRejection.None)
        {
            Publish();
        }

        return rejection;
    }

    public PlotBatchResult Apply(in CoverageSnapshot snapshot, ReadOnlySpan<PlotObservation> observations)
    {
        if (snapshot.Outcome == CoverageOutcome.Failed)
        {
            return new PlotBatchResult(MergeRejection.None, 0, 0, 0) { RecordedOutcome = CoverageOutcome.Failed };
        }

        for (var index = 0; index < observations.Length; index++)
        {
            var observation = observations[index];
            if (!snapshot.Scope.Contains(observation.Key) || observation.Provenance.Source != snapshot.Source)
            {
                return new PlotBatchResult(MergeRejection.IdentityMismatch, 0, observations.Length, 0) { FirstRowRejection = MergeRejection.IdentityMismatch };
            }
        }

        var reported = new HashSet<PlotKey>(observations.Length);
        var merged = 0;
        var firstRejection = MergeRejection.None;
        for (var index = 0; index < observations.Length; index++)
        {
            var observation = observations[index];
            reported.Add(observation.Key);
            var rejection = PlotMerge.Validate(observation);
            if (rejection == MergeRejection.None)
            {
                if (plots.TryGetValue(observation.Key, out var current))
                {
                    plots[observation.Key] = PlotMerge.ReleaseOmission(current, snapshot.Source, snapshot.ObservedFromUtc);
                }

                rejection = MergeRow(observation);
            }

            if (rejection == MergeRejection.None)
            {
                merged++;
            }
            else if (firstRejection == MergeRejection.None)
            {
                firstRejection = rejection;
            }
        }

        var rejected = observations.Length - merged;
        var outcome = rejected > 0 && snapshot.Outcome == CoverageOutcome.Complete ? CoverageOutcome.Partial : snapshot.Outcome;
        var marked = outcome == CoverageOutcome.Complete && snapshot.Source == ObservationSource.PublicFeed
            ? MarkUnreported(snapshot, reported)
            : 0;
        RecordCoverage(snapshot with { Outcome = outcome });
        Publish();
        return new PlotBatchResult(MergeRejection.None, merged, rejected, marked) { FirstRowRejection = firstRejection, RecordedOutcome = outcome };
    }

    private MergeRejection MergeRow(in PlotObservation observation)
    {
        plots.TryGetValue(observation.Key, out var current);
        var rejection = PlotMerge.TryMerge(current, observation, out var merged);
        if (rejection == MergeRejection.None)
        {
            plots[observation.Key] = merged;
        }

        return rejection;
    }

    private int MarkUnreported(in CoverageSnapshot snapshot, HashSet<PlotKey> reported)
    {
        var omission = new Provenance(snapshot.Source, snapshot.ObservedFromUtc, snapshot.ReceivedUtc, null);
        var marked = new List<PlotSnapshot>();
        foreach (var plot in plots.Values)
        {
            if (!snapshot.Scope.Contains(plot.Key) || reported.Contains(plot.Key))
            {
                continue;
            }

            if (PlotMerge.TryMarkNotReported(plot, omission, out var unreported))
            {
                marked.Add(unreported);
            }
        }

        for (var index = 0; index < marked.Count; index++)
        {
            plots[marked[index].Key] = marked[index];
        }

        return marked.Count;
    }

    private void RecordCoverage(in CoverageSnapshot snapshot)
    {
        var key = (snapshot.Scope, snapshot.Source);
        if (snapshot.Outcome == CoverageOutcome.Partial && coverage.TryGetValue(key, out var existing) && existing.Outcome == CoverageOutcome.Complete)
        {
            return;
        }

        coverage[key] = snapshot;
    }

    private void Publish()
    {
        Volatile.Write(ref published, plots.ToFrozenDictionary());
        Volatile.Write(ref publishedCoverage, coverage.ToFrozenDictionary());
        Interlocked.Increment(ref revision);
    }
}
