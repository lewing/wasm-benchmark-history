namespace WasmBenchmarkHistory.Data;

public static class TimeSeriesSegmenter
{
    public static IReadOnlyList<IReadOnlyList<T>> SplitAtLongGaps<T>(
        IReadOnlyList<T> points,
        Func<T, DateTime> getTimestamp,
        TimeSpan? minimumGap = null)
    {
        if (points.Count == 0)
        {
            return [];
        }

        var ordered = points
            .Select((point, index) => (point, index))
            .OrderBy(pair => getTimestamp(pair.point))
            .ThenBy(pair => pair.index)
            .Select(pair => pair.point)
            .ToArray();
        var positiveGaps = ordered
            .Zip(ordered.Skip(1))
            .Select(pair => getTimestamp(pair.Second) - getTimestamp(pair.First))
            .Where(gap => gap > TimeSpan.Zero)
            .Order()
            .ToArray();
        var medianGap = positiveGaps.Length == 0
            ? TimeSpan.Zero
            : positiveGaps[positiveGaps.Length / 2];
        var floor = minimumGap ?? TimeSpan.FromDays(2);
        var threshold = medianGap * 3 > floor
            ? medianGap * 3
            : floor;

        var segments = new List<IReadOnlyList<T>>();
        var current = new List<T> { ordered[0] };
        for (var index = 1; index < ordered.Length; index++)
        {
            if (getTimestamp(ordered[index]) - getTimestamp(ordered[index - 1]) > threshold)
            {
                segments.Add(current);
                current = [];
            }

            current.Add(ordered[index]);
        }

        segments.Add(current);
        return segments;
    }
}
