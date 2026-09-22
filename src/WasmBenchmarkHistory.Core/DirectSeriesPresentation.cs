using System.Globalization;

namespace WasmBenchmarkHistory.Data;

public sealed record DirectMarkerPresentation(
    string CssClass,
    string Role,
    string Shape,
    string AccessibleLabel);

public static class DirectSeriesPresentation
{
    public const double MarkerSize = 10;
    public const double MarkerHaloSize = 14;
    public const double MiniMarkerSize = 7;
    public const double MiniMarkerHaloSize = 11;

    public static IReadOnlyList<string> BuildConnectorPaths<T>(
        IReadOnlyList<T> points,
        Func<T, DateTime> getTimestamp,
        Func<T, string> formatPoint,
        TimeSpan? minimumGap = null)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(getTimestamp);
        ArgumentNullException.ThrowIfNull(formatPoint);

        return TimeSeriesSegmenter.SplitAtLongGaps(
                points,
                getTimestamp,
                minimumGap)
            .Where(segment => segment.Count >= 2)
            .Select(segment => string.Join(" ", segment.Select(formatPoint)))
            .ToArray();
    }

    public static DirectMarkerPresentation CreateMarker(
        string displayName,
        DateTime timestamp,
        string formattedValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(formattedValue);

        return new(
            "direct-marker",
            "img",
            "diamond",
            string.Format(
                CultureInfo.InvariantCulture,
                "{0} discrete direct snapshot at {1:yyyy-MM-dd HH:mm:ss}: {2}. " +
                "The dashed connector is a visual guide only.",
                displayName,
                timestamp,
                formattedValue));
    }
}
