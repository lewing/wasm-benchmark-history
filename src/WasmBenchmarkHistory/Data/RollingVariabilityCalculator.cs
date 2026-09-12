namespace WasmBenchmarkHistory.Data;

public readonly record struct VariabilitySample(
    DateTime Timestamp,
    double Value);

public sealed record VariabilityBandPoint(
    DateTime Timestamp,
    double Median,
    double FirstQuartile,
    double ThirdQuartile,
    int SampleCount)
{
    public double InterquartileRange => ThirdQuartile - FirstQuartile;
}

public static class RollingVariabilityCalculator
{
    public const int DefaultWindow = 15;

    public static IReadOnlyList<int> SupportedWindows { get; } = [7, 15, 31];

    public static bool IsSupportedWindow(int window) =>
        SupportedWindows.Contains(window);

    public static IReadOnlyList<VariabilityBandPoint> Calculate(
        IEnumerable<VariabilitySample> samples,
        int window = DefaultWindow)
    {
        if (!IsSupportedWindow(window))
        {
            throw new ArgumentOutOfRangeException(
                nameof(window),
                window,
                $"The rolling window must be one of: {string.Join(", ", SupportedWindows)}.");
        }

        var ordered = samples
            .Select((sample, index) => (sample, index))
            .OrderBy(pair => pair.sample.Timestamp)
            .ThenBy(pair => pair.index)
            .Select(pair => pair.sample)
            .ToArray();
        if (ordered.Length < window)
        {
            return [];
        }

        var result = new VariabilityBandPoint[ordered.Length - window + 1];
        var values = new double[window];
        for (var end = window - 1; end < ordered.Length; end++)
        {
            for (var index = 0; index < window; index++)
            {
                values[index] = ordered[end - window + 1 + index].Value;
            }

            Array.Sort(values);
            result[end - window + 1] = new VariabilityBandPoint(
                ordered[end].Timestamp,
                Percentile(values, .5),
                Percentile(values, .25),
                Percentile(values, .75),
                window);
        }

        return result;
    }

    private static double Percentile(
        IReadOnlyList<double> sortedValues,
        double percentile)
    {
        var position = (sortedValues.Count - 1) * percentile;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return sortedValues[lower];
        }

        return sortedValues[lower]
            + (sortedValues[upper] - sortedValues[lower]) * (position - lower);
    }
}
