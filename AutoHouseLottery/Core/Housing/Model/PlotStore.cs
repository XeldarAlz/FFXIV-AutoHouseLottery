namespace AutoHouseLottery.Core.Housing.Model;

internal sealed class PlotStore
{
    private readonly Dictionary<PlotKey, PlotSnapshot> plots = [];
    private readonly Dictionary<(CoverageScope Scope, ObservationSource Source), CoverageSnapshot> coverage = [];

    public IReadOnlyDictionary<PlotKey, PlotSnapshot> Plots => plots;

    public bool TryGetCoverage(in CoverageScope scope, ObservationSource source, out CoverageSnapshot snapshot)
    {
        return coverage.TryGetValue((scope, source), out snapshot);
    }

    public MergeRejection Merge(in PlotObservation observation)
    {
        plots.TryGetValue(observation.Key, out var current);
        var rejection = PlotMerge.TryMerge(current, observation, out var merged);
        if (rejection == MergeRejection.None)
        {
            plots[observation.Key] = merged;
        }

        return rejection;
    }

    public PlotBatchResult Apply(in CoverageSnapshot snapshot, ReadOnlySpan<PlotObservation> observations)
    {
        if (snapshot.Outcome == CoverageOutcome.Failed)
        {
            return default;
        }

        for (var index = 0; index < observations.Length; index++)
        {
            var observation = observations[index];
            if (!snapshot.Scope.Contains(observation.Key) || observation.Provenance.Source != snapshot.Source)
            {
                return new PlotBatchResult(MergeRejection.IdentityMismatch, 0, observations.Length, 0);
            }
        }

        var reported = new HashSet<PlotKey>(observations.Length);
        var merged = 0;
        for (var index = 0; index < observations.Length; index++)
        {
            reported.Add(observations[index].Key);
            if (Merge(observations[index]) == MergeRejection.None)
            {
                merged++;
            }
        }

        var marked = snapshot.Outcome == CoverageOutcome.Complete && snapshot.Source == ObservationSource.PublicFeed
            ? MarkUnreported(snapshot, reported)
            : 0;
        RecordCoverage(snapshot);
        return new PlotBatchResult(MergeRejection.None, merged, observations.Length - merged, marked);
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
}
