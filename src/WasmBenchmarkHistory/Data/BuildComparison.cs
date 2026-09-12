namespace WasmBenchmarkHistory.Data;

public static class BuildComparison
{
    public static readonly string[] LaneIds =
        ["mono-interpreter", "mono-aot", "coreclr-interpreter", "coreclr-r2r"];

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
        var rows = identities.Select(identity =>
        {
            var cells = LaneIds.ToDictionary(id => id, id =>
            {
                var values = groups[id].GetValueOrDefault(identity.Key) ?? [];
                var status = values.Length switch
                {
                    0 => "missing",
                    > 1 => "duplicate",
                    _ => InvalidReason(values[0].Statistics) is null && values[0].InvalidReason is null
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
        return new BuildComparisonResult(snapshot, rows, coverage);
    }

    public static PairwiseSpeedup[] Summarize(IEnumerable<BuildComparisonRow> rows)
    {
        // Every pair uses the same four-way intersection, not a different pairwise population.
        var common = rows.Where(row => row.IsCommon).ToArray();
        return LaneIds.SelectMany((baseline, index) => LaneIds.Skip(index + 1).Select(candidate =>
            new PairwiseSpeedup(baseline, candidate, common.Length,
                common.Length == 0 ? null : Math.Exp(common.Average(row =>
                    Math.Log(row.Cells[baseline].ValidMeasurement!.Statistics.Mean!.Value) -
                    Math.Log(row.Cells[candidate].ValidMeasurement!.Statistics.Mean!.Value))))))
            .ToArray();
    }

    public static string? InvalidReason(BenchmarkStatistics statistics)
    {
        if (statistics.Mean is not { } mean || !double.IsFinite(mean) || mean <= 0)
            return "Mean must be finite and positive.";
        if (statistics.N is not > 0)
            return "Statistics.N must be positive.";
        if (new[] { statistics.StandardDeviation, statistics.StandardError, statistics.Variance }
            .Any(value => value is { } number && (!double.IsFinite(number) || number < 0)))
            return "Variance statistics must be finite and non-negative.";
        return null;
    }

    public static void Validate(BuildSnapshot snapshot)
    {
        if (snapshot.SchemaVersion != 1)
            throw new InvalidDataException("Unsupported build snapshot schema.");
        if (snapshot.Lanes.Length != LaneIds.Length ||
            !snapshot.Lanes.Select(lane => lane.Provenance.Id).Order(StringComparer.Ordinal)
                .SequenceEqual(LaneIds.Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Exactly one of each of the four WASM runtime lanes is required.");
        if (string.IsNullOrWhiteSpace(snapshot.Build.BuildId) ||
            !IsSha(snapshot.Build.RuntimeSha) || !IsSha(snapshot.Build.PerformanceSha))
            throw new InvalidDataException("Build ID and full runtime/performance commit SHAs are required.");
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
