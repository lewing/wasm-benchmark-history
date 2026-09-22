namespace WasmBenchmarkHistory.Data;

public sealed record ChartHoverPoint(
    BenchmarkHistory History,
    BenchmarkObservation Observation,
    double DisplayValue,
    double? StrictRatio,
    bool IsExactTimestamp);

public sealed record ChartHoverSelection(
    int TimelineIndex,
    DateTime Timestamp,
    bool HasStrictComparison,
    IReadOnlyList<ChartHoverPoint> Points);

public sealed class ChartHoverIndex
{
    private readonly IReadOnlyList<BenchmarkHistory> _histories;
    private readonly IReadOnlyDictionary<string, BenchmarkObservation[]> _observationsByRun;
    private readonly IReadOnlyDictionary<DateTime, MatchedComparison> _matchesByTimestamp;
    private readonly bool _normalized;

    public ChartHoverIndex(
        IReadOnlyList<BenchmarkHistory> histories,
        IReadOnlyList<MatchedComparison> matches,
        bool normalized)
    {
        _histories = histories;
        _normalized = normalized;
        _observationsByRun = histories.ToDictionary(
            history => history.Run.Id,
            history => history.Observations
                .OrderBy(observation => observation.Timestamp)
                .ToArray(),
            StringComparer.Ordinal);

        var baselineId = histories.FirstOrDefault()?.Run.Id;
        _matchesByTimestamp = matches
            .OrderBy(match => match.Key.Timestamp)
            .ThenBy(match => match.Key.RuntimeSha, StringComparer.Ordinal)
            .ThenBy(match => match.Key.PerformanceSha, StringComparer.Ordinal)
            .GroupBy(match => match.Key.Timestamp)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single());

        Timestamps = (normalized
                ? _matchesByTimestamp.Values
                    .Where(match =>
                        baselineId is not null
                        && match.Observations.TryGetValue(baselineId, out var baseline)
                        && baseline.Value != 0)
                    .Select(match => match.Key.Timestamp)
                : histories.SelectMany(history =>
                    history.Observations.Select(observation => observation.Timestamp)))
            .Distinct()
            .Order()
            .ToArray();
    }

    public IReadOnlyList<DateTime> Timestamps { get; }

    public ChartHoverSelection? SelectNearest(DateTime target)
    {
        if (Timestamps.Count == 0)
        {
            return null;
        }

        return SelectAt(FindNearestIndex(Timestamps, target));
    }

    public ChartHoverSelection? SelectAt(int timelineIndex)
    {
        if (timelineIndex < 0 || timelineIndex >= Timestamps.Count)
        {
            return null;
        }

        var timestamp = Timestamps[timelineIndex];
        _matchesByTimestamp.TryGetValue(timestamp, out var strictMatch);
        var baseline = strictMatch is null || _histories.Count == 0
            ? null
            : strictMatch.Observations[_histories[0].Run.Id];
        var points = new List<ChartHoverPoint>(_histories.Count);

        foreach (var history in _histories)
        {
            BenchmarkObservation? observation;
            if (strictMatch is not null)
            {
                observation = strictMatch.Observations.GetValueOrDefault(history.Run.Id);
            }
            else
            {
                observation = FindNearest(
                    _observationsByRun.GetValueOrDefault(history.Run.Id) ?? [],
                    timestamp);
            }

            if (observation is null)
            {
                continue;
            }

            double? ratio = baseline is null || baseline.Value == 0
                ? null
                : observation.Value / baseline.Value;
            points.Add(new ChartHoverPoint(
                history,
                observation,
                _normalized && ratio is not null ? ratio.Value : observation.Value,
                ratio,
                observation.Timestamp == timestamp));
        }

        return new ChartHoverSelection(
            timelineIndex,
            timestamp,
            strictMatch is not null,
            points);
    }

    private static BenchmarkObservation? FindNearest(
        IReadOnlyList<BenchmarkObservation> observations,
        DateTime target)
    {
        if (observations.Count == 0)
        {
            return null;
        }

        return observations[FindNearestIndex(
            observations,
            target,
            observation => observation.Timestamp)];
    }

    private static int FindNearestIndex(IReadOnlyList<DateTime> values, DateTime target) =>
        FindNearestIndex(values, target, value => value);

    private static int FindNearestIndex<T>(
        IReadOnlyList<T> values,
        DateTime target,
        Func<T, DateTime> getTimestamp)
    {
        var low = 0;
        var high = values.Count - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var comparison = getTimestamp(values[middle]).CompareTo(target);
            if (comparison == 0)
            {
                return middle;
            }

            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        if (low == 0)
        {
            return 0;
        }

        if (low == values.Count)
        {
            return values.Count - 1;
        }

        var before = getTimestamp(values[low - 1]);
        var after = getTimestamp(values[low]);
        return target.Ticks - before.Ticks <= after.Ticks - target.Ticks
            ? low - 1
            : low;
    }
}
