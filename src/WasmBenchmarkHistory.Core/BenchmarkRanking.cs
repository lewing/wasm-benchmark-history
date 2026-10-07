namespace WasmBenchmarkHistory.Data;

public sealed record RankedBenchmark(
    BenchmarkIdentity Identity, double CandidateMean, double BaselineMean, double SlowdownPercent);

public sealed record BenchmarkRankingResult(
    RankedBenchmark[] Rows, int ComparableCount, int SlowerCount, int ExcludedCount);

public static class BenchmarkRanking
{
    public static BenchmarkRankingResult Create(
        BuildComparisonResult comparison, string candidateId, string baselineId, int limit, string? filter = null)
    {
        if (!BuildComparison.LaneIds.Contains(candidateId, StringComparer.Ordinal) ||
            !BuildComparison.LaneIds.Contains(baselineId, StringComparer.Ordinal))
            throw new ArgumentException("Choose two known runtime configurations.");
        foreach (var laneId in new[] { candidateId, baselineId })
            if (!comparison.LaneIds.Contains(laneId, StringComparer.Ordinal))
                throw new ArgumentException(
                    $"Build {comparison.Snapshot.Build.BuildId} has no {BuildComparison.DisplayName(laneId)} results.");
        if (candidateId == baselineId)
            throw new ArgumentException("Choose two different runtime configurations.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var slower = new List<RankedBenchmark>();
        var term = filter?.Trim() ?? "";
        var matching = 0;
        var comparable = 0;
        foreach (var row in comparison.Rows)
        {
            if (term.Length > 0 &&
                !row.Identity.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                !row.Categories.Any(category => category.Contains(term, StringComparison.OrdinalIgnoreCase)))
                continue;

            matching++;
            var candidate = row.Cells[candidateId].ValidMeasurement;
            var baseline = row.Cells[baselineId].ValidMeasurement;
            if (candidate is null || baseline is null)
                continue;

            var candidateMean = candidate.Statistics.Mean!.Value;
            var baselineMean = baseline.Statistics.Mean!.Value;
            var percent = (candidateMean - baselineMean) / baselineMean * 100;
            if (!double.IsFinite(percent))
                continue;

            comparable++;
            if (percent > 0)
                slower.Add(new(row.Identity, candidateMean, baselineMean, percent));
        }

        return new(
            slower.OrderByDescending(row => row.SlowdownPercent)
                .ThenBy(row => row.Identity.DisplayName, StringComparer.Ordinal)
                .ThenBy(row => row.Identity.Key, StringComparer.Ordinal)
                .Take(limit).ToArray(),
            comparable, slower.Count, matching - comparable);
    }
}
