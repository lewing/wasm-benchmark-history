using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class TimeSeriesSegmenterTests
{
    [Fact]
    public void SplitAtLongGaps_BreaksAfterLargeCalendarGap()
    {
        var start = new DateTime(2026, 1, 1);
        var points = new[]
        {
            start,
            start.AddHours(6),
            start.AddHours(12),
            start.AddDays(8),
            start.AddDays(8).AddHours(6)
        };

        var segments = TimeSeriesSegmenter.SplitAtLongGaps(
            points,
            timestamp => timestamp);

        Assert.Collection(
            segments,
            segment => Assert.Equal(3, segment.Count),
            segment => Assert.Equal(2, segment.Count));
    }

    [Fact]
    public void SplitAtLongGaps_PreservesDuplicateTimestampOrdering()
    {
        var timestamp = new DateTime(2026, 1, 1);
        var points = new[]
        {
            (Timestamp: timestamp.AddHours(1), Value: 3),
            (Timestamp: timestamp, Value: 1),
            (Timestamp: timestamp, Value: 2)
        };

        var segment = Assert.Single(TimeSeriesSegmenter.SplitAtLongGaps(
            points,
            point => point.Timestamp));

        Assert.Equal([1, 2, 3], segment.Select(point => point.Value));
    }
}
