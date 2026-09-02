using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class HistoryTimeRangeTests
{
    private static readonly DateTime Minimum = new(2025, 1, 1);
    private static readonly DateTime Maximum = new(2026, 1, 1);

    [Theory]
    [InlineData(HistoryRangeKind.ThirtyDays, 30)]
    [InlineData(HistoryRangeKind.NinetyDays, 90)]
    [InlineData(HistoryRangeKind.OneYear, 365)]
    public void PresetsResolveRelativeToLatestObservation(
        HistoryRangeKind kind,
        int expectedDays)
    {
        var range = new HistoryTimeRange(kind).Resolve(Minimum, Maximum);

        Assert.Equal(Maximum.AddDays(-expectedDays), range.Start);
        Assert.Equal(Maximum, range.End);
    }

    [Fact]
    public void CustomRangeClampsToAvailableHistory()
    {
        var range = HistoryTimeRange.Custom(
            Minimum.AddDays(-10),
            Maximum.AddDays(10));

        Assert.Equal((Minimum, Maximum), range.Resolve(Minimum, Maximum));
    }

    [Fact]
    public void CustomRangeOutsideHistoryFallsBackToAll()
    {
        var range = HistoryTimeRange.Custom(
            Minimum.AddYears(-2),
            Minimum.AddYears(-1));

        Assert.Equal((Minimum, Maximum), range.Resolve(Minimum, Maximum));
    }
}
