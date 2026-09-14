namespace WasmBenchmarkHistory.Data;

public static class DirectSnapshotHistory
{
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> GetAvailability(
        IEnumerable<DirectHistoryBuild> builds)
    {
        var availability = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var build in builds)
        {
            foreach (var lane in build.Lanes)
            {
                var runId = RunId(lane.Id);
                foreach (var measurement in ValidUnique(lane.Measurements))
                {
                    if (!availability.TryGetValue(measurement.Identity.DisplayName, out var runs))
                    {
                        runs = new HashSet<string>(StringComparer.Ordinal);
                        availability.Add(measurement.Identity.DisplayName, runs);
                    }
                    runs.Add(runId);
                }
            }
        }
        return Freeze(availability);
    }

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
        return Freeze(availability);
    }

    public static BenchmarkHistory? CreateHistory(
        string benchmark,
        RunConfiguration run,
        IEnumerable<DirectHistoryBuild> builds)
    {
        var laneId = LaneId(run.Id);
        var observations = new List<BenchmarkObservation>();
        foreach (var build in builds)
        {
            var lane = build.Lanes.Single(value => value.Id == laneId);
            var matches = lane.Measurements.Where(value =>
                value.Identity.DisplayName == benchmark && value.InvalidReason is null).ToArray();
            if (matches.Length > 1)
                throw Conflict(benchmark, run.Id, "duplicate direct history measurements");
            if (matches.Length == 0)
                continue;
            var timestamp = ParseTimestamp(build.Build);
            var measurement = matches[0];
            observations.Add(new BenchmarkObservation(
                benchmark,
                run.Id,
                timestamp,
                measurement.Mean!.Value,
                measurement.Error,
                build.Build.RuntimeSha,
                build.Build.PerformanceSha,
                $"{build.CaptureSource} · build {build.Build.BuildNumber}",
                ObservationSource.DirectSnapshot,
                build.Build.BuildId,
                null,
                measurement.Partition,
                build.CaptureSource));
        }
        return CreateHistory(benchmark, run, observations);
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
            var measurement = matches[0];
            observations.Add(new BenchmarkObservation(
                benchmark,
                run.Id,
                ParseTimestamp(snapshot.Build),
                measurement.Statistics.Mean!.Value,
                measurement.Statistics.StandardError,
                snapshot.Build.RuntimeSha,
                snapshot.Build.PerformanceSha,
                $"{snapshot.CaptureSource} · build {snapshot.Build.BuildNumber}",
                ObservationSource.DirectSnapshot,
                snapshot.Build.BuildId,
                measurement.Statistics.OriginalValues,
                measurement.Partition,
                snapshot.CaptureSource));
        }
        return CreateHistory(benchmark, run, observations);
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

    private static BenchmarkHistory? CreateHistory(
        string benchmark,
        RunConfiguration run,
        IReadOnlyList<BenchmarkObservation> observations)
    {
        if (observations.Count == 0)
            return null;
        var merged = MergeObservations(benchmark, run.Id, [], observations);
        return new BenchmarkHistory(
            benchmark, run, "Direct Helix snapshots", merged.Observations, merged.Conflicts);
    }

    private static IEnumerable<DirectHistoryMeasurement> ValidUnique(
        IEnumerable<DirectHistoryMeasurement> measurements) =>
        measurements.GroupBy(value => value.Identity.Key, StringComparer.Ordinal)
            .Where(group => group.Count() == 1 && group.Single().InvalidReason is null)
            .Select(group => group.Single());

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> Freeze(
        Dictionary<string, HashSet<string>> availability) =>
        availability.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlySet<string>)pair.Value,
            StringComparer.Ordinal);

    private static DateTime ParseTimestamp(BuildProvenance build)
    {
        if (!DateTimeOffset.TryParse(build.SourceDate,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var timestamp))
            throw new InvalidDataException($"Build {build.BuildId} has an invalid direct snapshot timestamp.");
        return DateTime.SpecifyKind(timestamp.UtcDateTime, DateTimeKind.Unspecified);
    }

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
