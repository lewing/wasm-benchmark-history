namespace WasmBenchmarkHistory.Data;

public enum HistoryBoundaryAssessment
{
    Stable,
    Noisy,
    Insufficient
}

public sealed record HistoryContextPoint(
    BenchmarkObservation Observation,
    RobustWindowSummary Nearby,
    RobustWindowSummary Trailing,
    double? RelativeVolatilityPercent);

public sealed record ChangeSetHistoryAnalysis(
    HistoryContextPoint Baseline,
    HistoryContextPoint Compare,
    ObservationDifference Difference,
    HistoryBoundaryAssessment Assessment,
    string BenchmarkViewUri);

public sealed record ChangeSetHistoryPreviewPoint(
    DateTime Timestamp,
    double Value,
    bool IsBaseline,
    bool IsCompare);

public sealed record ChangeSetHistoryPreview(
    IReadOnlyList<ChangeSetHistoryPreviewPoint> Points,
    DateTime Start,
    DateTime End);

public static class ChangeSetHistoryAnalyzer
{
    public const int ContextRadius = 3;
    public const int TrailingWindow = 7;
    public const int MinimumContextSamples = 5;
    public const double HighRelativeVolatilityPercent = 5;
    public const double StableSignalToNoiseMultiplier = 2;

    public static ChangeSetHistoryAnalysis Analyze(
        BenchmarkHistory history,
        string baselineRuntimeSha,
        string compareRuntimeSha,
        string? performanceSha = null)
    {
        var baseline = ResolveExact(history, baselineRuntimeSha, performanceSha, "baseline");
        var compare = ResolveExact(history, compareRuntimeSha, performanceSha, "compare");
        var baselinePin = new InvestigationPin(
            history.Run.Id,
            ObservationIdentity.From(baseline));
        var comparePin = new InvestigationPin(
            history.Run.Id,
            ObservationIdentity.From(compare));
        var difference = RegressionInvestigation.ComparePoints(baseline, compare);
        var baselineContext = Context(history, baselinePin);
        var compareContext = Context(history, comparePin);
        var assessment = Assess(baselineContext, compareContext, difference);
        var state = new HistoryPageState(
            history.Benchmark,
            KnownRunConfigurations.All.Select(run => run.Id).ToArray(),
            false,
            HistoryTimeRange.All,
            history.Run.Id,
            baselinePin.Identity,
            comparePin.Identity,
            true,
            true,
            RollingVariabilityCalculator.DefaultWindow);

        return new ChangeSetHistoryAnalysis(
            baselineContext,
            compareContext,
            difference,
            assessment,
            HistoryPageStateCodec.ToRelativeUri(state));
    }

    public static ChangeSetHistoryPreview CreatePreview(
        BenchmarkHistory history,
        string baselineRuntimeSha,
        string compareRuntimeSha,
        string? performanceSha = null,
        int maximumPoints = 60)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumPoints, 10);

        var baseline = ResolveExact(history, baselineRuntimeSha, performanceSha, "baseline");
        var compare = ResolveExact(history, compareRuntimeSha, performanceSha, "compare");
        var ordered = history.Observations
            .OrderBy(observation => observation.Timestamp)
            .ThenBy(observation => observation.RuntimeSha, StringComparer.Ordinal)
            .ThenBy(observation => observation.PerformanceSha, StringComparer.Ordinal)
            .ToArray();
        var baselineIdentity = ObservationIdentity.From(baseline);
        var compareIdentity = ObservationIdentity.From(compare);
        var baselineIndex = Array.FindIndex(
            ordered,
            observation => ObservationIdentity.From(observation) == baselineIdentity);
        var compareIndex = Array.FindIndex(
            ordered,
            observation => ObservationIdentity.From(observation) == compareIdentity);
        var start = Math.Max(0, Math.Min(baselineIndex, compareIndex) - 10);
        var end = Math.Min(ordered.Length - 1, Math.Max(baselineIndex, compareIndex) + 10);
        var indexes = SampleIndexes(start, end, maximumPoints);
        indexes.Add(baselineIndex);
        indexes.Add(compareIndex);
        var points = indexes
            .Order()
            .Select(index =>
            {
                var observation = ordered[index];
                var identity = ObservationIdentity.From(observation);
                return new ChangeSetHistoryPreviewPoint(
                    observation.Timestamp,
                    observation.Value,
                    identity == baselineIdentity,
                    identity == compareIdentity);
            })
            .ToArray();

        return new ChangeSetHistoryPreview(
            points,
            ordered[start].Timestamp,
            ordered[end].Timestamp);
    }

    internal static BenchmarkObservation ResolveExact(
        BenchmarkHistory history,
        string runtimeSha,
        string? performanceSha,
        string label)
    {
        if (!GitHubCompareLinkBuilder.IsValidSha(runtimeSha))
        {
            throw new BenchmarkDataException(
                BenchmarkDataError.UnknownBenchmark,
                $"The imported {label} runtime SHA is invalid.");
        }

        var matches = history.Observations
            .Where(observation =>
                observation.RuntimeSha.Equals(runtimeSha, StringComparison.OrdinalIgnoreCase)
                && (performanceSha is null
                    || observation.PerformanceSha.Equals(
                        performanceSha,
                        StringComparison.OrdinalIgnoreCase)))
            .Take(2)
            .ToArray();
        if (matches.Length == 0)
        {
            throw new BenchmarkDataException(
                BenchmarkDataError.UnknownBenchmark,
                $"The exact {label} runtime SHA was not found in this history.");
        }

        if (matches.Length > 1)
        {
            throw new BenchmarkDataException(
                BenchmarkDataError.UnknownBenchmark,
                $"The exact {label} runtime SHA is ambiguous in this history"
                + (performanceSha is null ? "; no performance SHA was available to disambiguate it." : "."));
        }

        return matches[0];
    }

    private static HashSet<int> SampleIndexes(
        int start,
        int end,
        int maximumPoints)
    {
        var count = end - start + 1;
        if (count <= maximumPoints)
        {
            return Enumerable.Range(start, count).ToHashSet();
        }

        var indexes = new HashSet<int>();
        for (var index = 0; index < maximumPoints; index++)
        {
            indexes.Add((int)Math.Round(
                start + (double)(end - start) * index / (maximumPoints - 1)));
        }

        return indexes;
    }

    private static HistoryContextPoint Context(
        BenchmarkHistory history,
        InvestigationPin pin)
    {
        var nearby = RegressionInvestigation.SummarizeWindow(history, pin, ContextRadius)
            ?? throw new BenchmarkDataException(
                BenchmarkDataError.UnknownBenchmark,
                "The exact history observation was missing or ambiguous.");
        var ordered = history.Observations
            .OrderBy(observation => observation.Timestamp)
            .ThenBy(observation => observation.RuntimeSha, StringComparer.Ordinal)
            .ThenBy(observation => observation.PerformanceSha, StringComparer.Ordinal)
            .ToArray();
        var index = Array.FindIndex(
            ordered,
            observation => ObservationIdentity.From(observation) == pin.Identity);
        var start = Math.Max(0, index - TrailingWindow + 1);
        var values = ordered[start..(index + 1)]
            .Select(observation => observation.Value)
            .Order()
            .ToArray();
        var trailing = Summary(values);
        double? relativeVolatility = trailing.Median == 0
            ? null
            : trailing.InterquartileRange / Math.Abs(trailing.Median) * 100;
        return new HistoryContextPoint(
            ordered[index],
            nearby,
            trailing,
            relativeVolatility);
    }

    private static HistoryBoundaryAssessment Assess(
        HistoryContextPoint baseline,
        HistoryContextPoint compare,
        ObservationDifference difference)
    {
        if (baseline.Trailing.Count < MinimumContextSamples
            || compare.Trailing.Count < MinimumContextSamples
            || difference.PercentChange is null
            || baseline.RelativeVolatilityPercent is null
            || compare.RelativeVolatilityPercent is null)
        {
            return HistoryBoundaryAssessment.Insufficient;
        }

        var volatility = Math.Max(
            baseline.RelativeVolatilityPercent.Value,
            compare.RelativeVolatilityPercent.Value);
        if (volatility >= HighRelativeVolatilityPercent
            || Math.Abs(difference.PercentChange.Value)
                < volatility * StableSignalToNoiseMultiplier)
        {
            return HistoryBoundaryAssessment.Noisy;
        }

        return HistoryBoundaryAssessment.Stable;
    }

    private static RobustWindowSummary Summary(IReadOnlyList<double> sortedValues) =>
        new(
            sortedValues.Count,
            Percentile(sortedValues, .5),
            Percentile(sortedValues, .25),
            Percentile(sortedValues, .75));

    private static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        var position = (values.Count - 1) * percentile;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        return lower == upper
            ? values[lower]
            : values[lower] + (values[upper] - values[lower]) * (position - lower);
    }
}
