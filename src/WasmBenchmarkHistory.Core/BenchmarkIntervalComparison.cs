namespace WasmBenchmarkHistory.Data;

public enum IntervalSampleStatus
{
    Available,
    NoObservations,
    TooDistant,
    Ambiguous
}

public sealed record IntervalSample(
    BenchmarkObservation? Observation,
    TimeSpan Offset,
    TimeSpan MaximumOffset,
    IntervalSampleStatus Status);

public sealed record BenchmarkIntervalComparison(
    string Benchmark,
    string RunId,
    RunConfiguration Run,
    IntervalSample AtA,
    IntervalSample AtB,
    RegressionInvestigationResult? Analysis);

public static class BenchmarkIntervalComparisonCalculator
{
    private static readonly TimeSpan MinimumSampleOffset = TimeSpan.FromDays(7);
    private static readonly TimeSpan MaximumSampleOffset = TimeSpan.FromDays(30);

    public static BenchmarkIntervalComparison Analyze(
        BenchmarkHistory history,
        DateTime timestampA,
        DateTime timestampB,
        int windowRadius = RegressionInvestigation.DefaultWindowRadius)
    {
        var maximumOffset = GetMaximumSampleOffset(history);
        var atA = FindSample(history, timestampA, maximumOffset);
        var atB = FindSample(history, timestampB, maximumOffset);
        var analysis = atA.Observation is not null && atB.Observation is not null
            ? RegressionInvestigation.Analyze(
                history,
                Pin(atA.Observation),
                Pin(atB.Observation),
                windowRadius)
            : null;

        return new BenchmarkIntervalComparison(
            history.Benchmark,
            history.Run.Id,
            history.Run,
            atA,
            atB,
            analysis);
    }

    private static IntervalSample FindSample(
        BenchmarkHistory history,
        DateTime target,
        TimeSpan maximumOffset)
    {
        if (history.Observations.Count == 0)
        {
            return new IntervalSample(null, TimeSpan.Zero, maximumOffset, IntervalSampleStatus.NoObservations);
        }

        var nearest = history.Observations
            .Select(observation => (
                Observation: observation,
                OffsetTicks: Math.Abs(observation.Timestamp.Ticks - target.Ticks)))
            .OrderBy(candidate => candidate.OffsetTicks)
            .Take(2)
            .ToArray();
        var offset = TimeSpan.FromTicks(nearest[0].OffsetTicks);
        if (nearest.Length > 1 && nearest[0].OffsetTicks == nearest[1].OffsetTicks)
        {
            return new IntervalSample(null, offset, maximumOffset, IntervalSampleStatus.Ambiguous);
        }

        if (offset > maximumOffset)
        {
            return new IntervalSample(null, offset, maximumOffset, IntervalSampleStatus.TooDistant);
        }

        return new IntervalSample(
            nearest[0].Observation,
            offset,
            maximumOffset,
            IntervalSampleStatus.Available);
    }

    private static TimeSpan GetMaximumSampleOffset(BenchmarkHistory history)
    {
        var timestamps = history.Observations
            .Select(observation => observation.Timestamp.Ticks)
            .Distinct()
            .Order()
            .ToArray();
        if (timestamps.Length < 2)
        {
            return MinimumSampleOffset;
        }

        var intervals = timestamps
            .Zip(timestamps.Skip(1), (left, right) => right - left)
            .Order()
            .ToArray();
        var middle = intervals.Length / 2;
        var cadence = intervals.Length % 2 == 0
            ? intervals[middle - 1] + (intervals[middle] - intervals[middle - 1]) / 2
            : intervals[middle];
        var toleranceTicks = cadence > MaximumSampleOffset.Ticks / 3
            ? MaximumSampleOffset.Ticks
            : cadence * 3;
        return TimeSpan.FromTicks(Math.Clamp(
            toleranceTicks,
            MinimumSampleOffset.Ticks,
            MaximumSampleOffset.Ticks));
    }

    private static InvestigationPin Pin(BenchmarkObservation observation) =>
        new(observation.RunId, ObservationIdentity.From(observation));
}
