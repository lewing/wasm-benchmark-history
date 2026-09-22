using System.Text.RegularExpressions;

namespace WasmBenchmarkHistory.Data;

public enum InvestigationPinSlot
{
    BaselineA,
    ComparisonB
}

public readonly record struct ObservationIdentity(
    DateTime Timestamp,
    string RuntimeSha,
    string PerformanceSha)
{
    public static ObservationIdentity From(BenchmarkObservation observation) =>
        new(
            observation.Timestamp,
            observation.RuntimeSha,
            observation.PerformanceSha);
}

public sealed record InvestigationPin(
    string RunId,
    ObservationIdentity Identity);

public sealed record InvestigationPinRequest(
    InvestigationPinSlot Slot,
    InvestigationPin Pin);

public sealed record ObservationDifference(
    BenchmarkObservation Older,
    BenchmarkObservation Newer,
    InvestigationPinSlot OlderSlot,
    bool PinsWereReordered,
    double AbsoluteDelta,
    double? PercentChange,
    double? Ratio);

public sealed record RobustWindowSummary(
    int Count,
    double Median,
    double FirstQuartile,
    double ThirdQuartile)
{
    public double InterquartileRange => ThirdQuartile - FirstQuartile;
}

public sealed record RobustWindowDifference(
    RobustWindowSummary Older,
    RobustWindowSummary Newer,
    double AbsoluteDelta,
    double? PercentChange,
    double? Ratio);

public sealed record RegressionInvestigationResult(
    ObservationDifference Points,
    RobustWindowDifference Windows,
    int WindowRadius);

public static class RegressionInvestigation
{
    public const int DefaultWindowRadius = 3;

    public static BenchmarkObservation? Resolve(
        BenchmarkHistory history,
        InvestigationPin pin)
    {
        if (!string.Equals(history.Run.Id, pin.RunId, StringComparison.Ordinal))
        {
            return null;
        }

        var matches = history.Observations
            .Where(observation => ObservationIdentity.From(observation) == pin.Identity)
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public static RegressionInvestigationResult? Analyze(
        BenchmarkHistory history,
        InvestigationPin baselineA,
        InvestigationPin comparisonB,
        int windowRadius = DefaultWindowRadius)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(windowRadius);

        var observationA = Resolve(history, baselineA);
        var observationB = Resolve(history, comparisonB);
        if (observationA is null || observationB is null)
        {
            return null;
        }

        var comparison = ComparePoints(observationA, observationB);
        var olderPin = comparison.OlderSlot == InvestigationPinSlot.BaselineA
            ? baselineA
            : comparisonB;
        var newerPin = comparison.OlderSlot == InvestigationPinSlot.BaselineA
            ? comparisonB
            : baselineA;
        var olderWindow = SummarizeWindow(history, olderPin, windowRadius);
        var newerWindow = SummarizeWindow(history, newerPin, windowRadius);
        if (olderWindow is null || newerWindow is null)
        {
            return null;
        }

        return new RegressionInvestigationResult(
            comparison,
            CompareWindows(olderWindow, newerWindow),
            windowRadius);
    }

    public static ObservationDifference ComparePoints(
        BenchmarkObservation baselineA,
        BenchmarkObservation comparisonB)
    {
        var reorder = CompareIdentity(baselineA, comparisonB) > 0;
        var older = reorder ? comparisonB : baselineA;
        var newer = reorder ? baselineA : comparisonB;
        var olderSlot = reorder
            ? InvestigationPinSlot.ComparisonB
            : InvestigationPinSlot.BaselineA;
        var absoluteDelta = newer.Value - older.Value;
        double? ratio = older.Value == 0 ? null : newer.Value / older.Value;

        return new ObservationDifference(
            older,
            newer,
            olderSlot,
            reorder,
            absoluteDelta,
            ratio is null ? null : (ratio.Value - 1) * 100,
            ratio);
    }

    public static RobustWindowSummary? SummarizeWindow(
        BenchmarkHistory history,
        InvestigationPin pin,
        int windowRadius = DefaultWindowRadius)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(windowRadius);

        var ordered = history.Observations
            .OrderBy(observation => observation.Timestamp)
            .ThenBy(observation => observation.RuntimeSha, StringComparer.Ordinal)
            .ThenBy(observation => observation.PerformanceSha, StringComparer.Ordinal)
            .ToArray();
        var matchingIndexes = ordered
            .Select((observation, index) => (observation, index))
            .Where(pair => ObservationIdentity.From(pair.observation) == pin.Identity)
            .Take(2)
            .Select(pair => pair.index)
            .ToArray();
        if (matchingIndexes.Length != 1)
        {
            return null;
        }

        var first = Math.Max(0, matchingIndexes[0] - windowRadius);
        var last = Math.Min(ordered.Length - 1, matchingIndexes[0] + windowRadius);
        var values = ordered[first..(last + 1)]
            .Select(observation => observation.Value)
            .Order()
            .ToArray();

        return new RobustWindowSummary(
            values.Length,
            Percentile(values, .5),
            Percentile(values, .25),
            Percentile(values, .75));
    }

    private static RobustWindowDifference CompareWindows(
        RobustWindowSummary older,
        RobustWindowSummary newer)
    {
        var absoluteDelta = newer.Median - older.Median;
        double? ratio = older.Median == 0 ? null : newer.Median / older.Median;
        return new RobustWindowDifference(
            older,
            newer,
            absoluteDelta,
            ratio is null ? null : (ratio.Value - 1) * 100,
            ratio);
    }

    private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
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

    private static int CompareIdentity(
        BenchmarkObservation left,
        BenchmarkObservation right)
    {
        var timestampComparison = left.Timestamp.CompareTo(right.Timestamp);
        if (timestampComparison != 0)
        {
            return timestampComparison;
        }

        var runtimeComparison = string.Compare(
            left.RuntimeSha,
            right.RuntimeSha,
            StringComparison.Ordinal);
        return runtimeComparison != 0
            ? runtimeComparison
            : string.Compare(
                left.PerformanceSha,
                right.PerformanceSha,
                StringComparison.Ordinal);
    }
}

public static partial class GitHubCompareLinkBuilder
{
    public static Uri? Runtime(string olderSha, string newerSha) =>
        Create("runtime", olderSha, newerSha);

    public static Uri? Performance(string olderSha, string newerSha) =>
        Create("performance", olderSha, newerSha);

    public static bool IsValidSha(string? sha) =>
        sha is not null && ShaPattern().IsMatch(sha);

    private static Uri? Create(string repository, string olderSha, string newerSha)
    {
        if (!IsValidSha(olderSha)
            || !IsValidSha(newerSha)
            || string.Equals(olderSha, newerSha, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new Uri(
            $"https://github.com/dotnet/{repository}/compare/{Uri.EscapeDataString(olderSha)}...{Uri.EscapeDataString(newerSha)}");
    }

    [GeneratedRegex(@"\A[0-9a-fA-F]{7,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ShaPattern();
}
