namespace WasmBenchmarkHistory.Data;

public static class BuildComparison
{
    public const string CoreClrR2RComposite = "coreclr-r2r-composite";

    // Lanes every build snapshot must contain.
    public static readonly string[] RequiredLaneIds =
        ["mono-interpreter", "mono-aot", "coreclr-interpreter", "coreclr-r2r"];

    // Newer lanes that older builds predate; used only when a build contains them.
    public static readonly string[] OptionalLaneIds = [CoreClrR2RComposite];

    // Every known lane, in canonical display order.
    public static readonly string[] LaneIds = [.. RequiredLaneIds, .. OptionalLaneIds];

    public static bool IsOptional(string laneId) => OptionalLaneIds.Contains(laneId, StringComparer.Ordinal);

    public static string DisplayName(string laneId) => laneId switch
    {
        "mono-interpreter" => "Mono interpreter",
        "mono-aot" => "Mono AOT",
        "coreclr-interpreter" => "CoreCLR interpreter",
        "coreclr-r2r" => "CoreCLR R2R",
        CoreClrR2RComposite => "CoreCLR R2R composite",
        _ => throw new ArgumentException($"Unknown runtime lane '{laneId}'.", nameof(laneId))
    };

    public static string[] LaneIdsOf(BuildSnapshot snapshot) =>
        Canonical(snapshot.Lanes.Select(lane => lane.Provenance.Id));

    public static string[] Canonical(IEnumerable<string> laneIds)
    {
        var present = laneIds.ToHashSet(StringComparer.Ordinal);
        return LaneIds.Where(present.Contains).ToArray();
    }

    // Validates a lane set: each required lane exactly once, optional lanes at most once, nothing unknown.
    public static string? LaneSetError(IReadOnlyCollection<string> laneIds)
    {
        if (laneIds.Distinct(StringComparer.Ordinal).Count() != laneIds.Count)
            return "Each Wasm runtime lane may appear only once.";
        var unknown = laneIds.Where(id => !LaneIds.Contains(id, StringComparer.Ordinal)).ToArray();
        if (unknown.Length > 0)
            return $"Unknown Wasm runtime lanes: {string.Join(", ", unknown)}.";
        var missing = RequiredLaneIds.Where(id => !laneIds.Contains(id, StringComparer.Ordinal)).ToArray();
        return missing.Length > 0
            ? $"Every build requires the four core Wasm runtime lanes; missing {string.Join(", ", missing)}."
            : null;
    }

    public static BuildComparisonResult Analyze(BuildSnapshot snapshot)
    {
        Validate(snapshot);
        var groups = snapshot.Lanes.ToDictionary(
            lane => lane.Provenance.Id,
            lane => lane.Measurements.GroupBy(value => value.Identity.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal));
        var identities = snapshot.Lanes.SelectMany(lane => lane.Measurements)
            .Select(value => value.Identity).DistinctBy(identity => identity.Key)
            .OrderBy(identity => identity.DisplayName, StringComparer.Ordinal).ToArray();
        var laneIds = LaneIdsOf(snapshot);
        var rows = identities.Select(identity =>
        {
            var cells = laneIds.ToDictionary(id => id, id =>
            {
                var values = groups[id].GetValueOrDefault(identity.Key) ?? [];
                var status = values.Length switch
                {
                    0 => "missing",
                    > 1 => "duplicate",
                    _ => InvalidReason(values[0].Statistics, requireSampleCount: !snapshot.IsPublishedHistory) is null && values[0].InvalidReason is null
                        ? "valid" : "invalid"
                };
                return new ComparisonCell(status, values);
            });
            var categories = cells.Values.SelectMany(cell => cell.Measurements)
                .SelectMany(value => value.Categories).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray();
            return new BuildComparisonRow(identity, categories, cells);
        }).ToArray();
        var coverage = snapshot.Lanes.Select(lane =>
        {
            var id = lane.Provenance.Id;
            return new LaneCoverage(id, lane.Measurements.Length, groups[id].Count,
                rows.Count(row => row.Cells[id].Status == "valid"),
                rows.Count(row => row.Cells[id].Status == "missing"),
                rows.Count(row => row.Cells[id].Status == "invalid"),
                rows.Count(row => row.Cells[id].Status == "duplicate"),
                lane.Partitions.Sum(partition => partition.Unidentified),
                lane.Partitions.Length, lane.Provenance.ExpectedPartitions);
        }).ToArray();
        return new BuildComparisonResult(snapshot, rows,
            coverage.OrderBy(value => Array.IndexOf(LaneIds, value.LaneId)).ToArray());
    }

    public static PairwiseSpeedup[] Summarize(
        IEnumerable<BuildComparisonRow> rows, IReadOnlyList<string>? laneIds = null)
    {
        // Every pair uses the same all-lane intersection, not a different pairwise population.
        var all = rows.ToArray();
        var lanes = laneIds ?? (all.Length > 0 ? Canonical(all[0].Cells.Keys) : RequiredLaneIds);
        var common = all.Where(row => row.IsCommon).ToArray();
        return lanes.SelectMany((baseline, index) => lanes.Skip(index + 1).Select(candidate =>
            new PairwiseSpeedup(baseline, candidate, common.Length,
                common.Length == 0 ? null : Math.Exp(common.Average(row =>
                    Math.Log(row.Cells[baseline].ValidMeasurement!.Statistics.Mean!.Value) -
                    Math.Log(row.Cells[candidate].ValidMeasurement!.Statistics.Mean!.Value))))))
            .ToArray();
    }

    public static string? InvalidReason(BenchmarkStatistics statistics, bool requireSampleCount = true)
    {
        if (statistics.Mean is not { } mean || !double.IsFinite(mean) || mean <= 0)
            return "Mean must be finite and positive.";
        if (requireSampleCount && statistics.N is not > 0)
            return "Statistics.N must be positive.";
        if (new[] { statistics.StandardDeviation, statistics.StandardError, statistics.Variance }
            .Any(value => value is { } number && (!double.IsFinite(number) || number < 0)))
            return "Variance statistics must be finite and non-negative.";
        return null;
    }

    public static void Validate(BuildSnapshot snapshot)
    {
        if (snapshot.SchemaVersion is not (1 or 2))
            throw new InvalidDataException("Unsupported build snapshot schema.");
        if (LaneSetError(snapshot.Lanes.Select(lane => lane.Provenance.Id).ToArray()) is { } laneError)
            throw new InvalidDataException(laneError);
        if (string.IsNullOrWhiteSpace(snapshot.Build.BuildId) ||
            !IsSha(snapshot.Build.RuntimeSha) || !IsSha(snapshot.Build.PerformanceSha))
            throw new InvalidDataException("Build ID and full runtime/performance commit SHAs are required.");
        if (snapshot.IsPublishedHistory)
        {
            PublishedBuildSnapshot.Validate(snapshot);
            return;
        }
        if (snapshot.PublishedIdentity is not null)
            throw new InvalidDataException("A direct snapshot cannot declare a published-history identity.");
        foreach (var lane in snapshot.Lanes)
        {
            if (lane.Provenance.Build != snapshot.Build)
                throw new InvalidDataException($"Lane '{lane.Provenance.Id}' is from a different build.");
            if (lane.Provenance.ExpectedPartitions <= 0 ||
                lane.Partitions.Length > lane.Provenance.ExpectedPartitions ||
                lane.Partitions.Select(partition => partition.Name).Distinct(StringComparer.Ordinal).Count()
                    != lane.Partitions.Length)
                throw new InvalidDataException($"Invalid partition inventory for '{lane.Provenance.Id}'.");
            if (lane.Partitions.Any(partition => string.IsNullOrWhiteSpace(partition.Name) ||
                    string.IsNullOrWhiteSpace(partition.Status) || partition.Reports < 0 ||
                    partition.Measurements < 0 || partition.Unidentified < 0 ||
                    (partition.Reports == 0 && string.IsNullOrWhiteSpace(partition.Note))))
                throw new InvalidDataException($"Incomplete partition coverage for '{lane.Provenance.Id}'.");
            if (lane.Partitions.Sum(partition => partition.Measurements) != lane.Measurements.Length ||
                lane.Measurements.Any(value => !lane.Partitions.Any(partition => partition.Name == value.Partition)))
                throw new InvalidDataException($"Measurements disagree with partition inventory for '{lane.Provenance.Id}'.");
        }
    }

    private static bool IsSha(string value) =>
        value is { Length: 40 } && value.All(char.IsAsciiHexDigit);
}
