namespace WasmBenchmarkHistory.Data;

public static class DirectSnapshotHistory
{
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> GetAvailability(
        IEnumerable<BuildSnapshot> snapshots)
    {
        var availability = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var snapshot in snapshots)
        {
            var result = BuildComparison.Analyze(snapshot);
            foreach (var lane in snapshot.Lanes)
            {
                var runId = RunId(lane.Provenance.Id);
                foreach (var row in result.Rows.Where(row => row.Cells[lane.Provenance.Id].Status == "valid"))
                {
                    if (!availability.TryGetValue(row.Identity.DisplayName, out var runs))
                    {
                        runs = new HashSet<string>(StringComparer.Ordinal);
                        availability.Add(row.Identity.DisplayName, runs);
                    }
                    runs.Add(runId);
                }
            }
        }
        return availability.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlySet<string>)pair.Value,
            StringComparer.Ordinal);
    }

    public static BenchmarkHistory? CreateHistory(
        string benchmark,
        RunConfiguration run,
        IEnumerable<BuildSnapshot> snapshots)
    {
        var laneId = LaneId(run.Id);
        var observations = new List<BenchmarkObservation>();
        foreach (var snapshot in snapshots)
        {
            var lane = snapshot.Lanes.Single(value => value.Provenance.Id == laneId);
            var matches = lane.Measurements.Where(value =>
                value.Identity.DisplayName == benchmark &&
                value.InvalidReason is null &&
                BuildComparison.InvalidReason(value.Statistics) is null).ToArray();
            if (matches.Length > 1)
                throw Conflict(benchmark, run.Id, "duplicate direct snapshot measurements");
            if (matches.Length == 0)
                continue;
            if (!DateTimeOffset.TryParse(snapshot.Build.SourceDate,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out var timestamp))
                throw Conflict(benchmark, run.Id, "invalid direct snapshot timestamp");
            var measurement = matches[0];
            observations.Add(new BenchmarkObservation(
                benchmark,
                run.Id,
                DateTime.SpecifyKind(timestamp.UtcDateTime, DateTimeKind.Unspecified),
                measurement.Statistics.Mean!.Value,
                measurement.Statistics.StandardError,
                snapshot.Build.RuntimeSha,
                snapshot.Build.PerformanceSha,
                $"{snapshot.CaptureSource} · build {snapshot.Build.BuildNumber}",
                ObservationSource.DirectSnapshot,
                snapshot.Build.BuildId,
                measurement.Statistics.OriginalValues));
        }
        if (observations.Count == 0)
            return null;
        var merged = MergeObservations(benchmark, run.Id, [], observations);
        return new BenchmarkHistory(
            benchmark, run, "Direct Helix snapshots", merged.Observations, merged.Conflicts);
    }

    public static ObservationMergeResult MergeObservations(
        string benchmark,
        string runId,
        IEnumerable<BenchmarkObservation> published,
        IEnumerable<BenchmarkObservation> direct)
    {
        var publishedValues = published.OrderBy(value => value.Timestamp).ToArray();
        var publishedByKey = publishedValues
            .GroupBy(Key)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var directValues = new Dictionary<ObservationKey, BenchmarkObservation>();
        var conflicts = new List<string>();
        foreach (var observation in direct.OrderBy(value => value.Timestamp))
        {
            var key = Key(observation);
            if (publishedByKey.TryGetValue(key, out var publishedMatches))
            {
                if (publishedMatches.Length != 1 ||
                    !EquivalentValue(publishedMatches[0], observation))
                    conflicts.Add(ConflictMessage(runId, key.Timestamp));
                continue;
            }
            if (directValues.TryGetValue(key, out var existing))
            {
                if (!EquivalentValue(existing, observation) ||
                    !Same(existing.Error, observation.Error) ||
                    existing.BuildId != observation.BuildId)
                    conflicts.Add(ConflictMessage(runId, key.Timestamp));
                continue;
            }
            directValues.Add(key, observation);
        }
        return new(
            publishedValues.Concat(directValues.Values).OrderBy(value => value.Timestamp).ToArray(),
            conflicts.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static ObservationKey Key(BenchmarkObservation observation) =>
        new(observation.Timestamp, observation.RuntimeSha, observation.PerformanceSha);

    private static bool EquivalentValue(BenchmarkObservation left, BenchmarkObservation right) =>
        left.Benchmark == right.Benchmark &&
        left.RunId == right.RunId &&
        Math.Abs(left.Value - right.Value) <= Math.Max(.005, Math.Abs(left.Value) * 1e-8);

    private static string ConflictMessage(string runId, DateTime timestamp) =>
        $"Published/direct data conflict for {runId} at {timestamp:yyyy-MM-dd HH:mm:ss}: " +
        "the value or duplicate cardinality differs; the published point is shown.";

    public sealed record ObservationMergeResult(
        IReadOnlyList<BenchmarkObservation> Observations,
        IReadOnlyList<string> Conflicts);

    private static bool Same(double? left, double? right) =>
        left is null && right is null ||
        left is { } leftValue && right is { } rightValue &&
        Math.Abs(leftValue - rightValue) <=
            Math.Max(Math.Abs(leftValue), Math.Abs(rightValue)) * 1e-12;

    private static BenchmarkDataException Conflict(string benchmark, string runId, string detail) =>
        new(BenchmarkDataError.Schema,
            $"Direct snapshot conflict for '{benchmark}' in '{runId}': {detail}.");

    public static string RunId(string laneId) => laneId switch
    {
        "mono-interpreter" => "mono-wasm",
        "mono-aot" => "mono-wasm-aot",
        "coreclr-interpreter" => "coreclr-wasm",
        "coreclr-r2r" => "coreclr-wasm-r2r",
        _ => throw new ArgumentException($"Unknown snapshot lane '{laneId}'.", nameof(laneId))
    };

    public static string LaneId(string runId) => runId switch
    {
        "mono-wasm" => "mono-interpreter",
        "mono-wasm-aot" => "mono-aot",
        "coreclr-wasm" => "coreclr-interpreter",
        "coreclr-wasm-r2r" => "coreclr-r2r",
        _ => throw new ArgumentException($"Unknown run configuration '{runId}'.", nameof(runId))
    };
}
