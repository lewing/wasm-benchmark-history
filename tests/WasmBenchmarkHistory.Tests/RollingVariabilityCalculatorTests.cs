using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class RollingVariabilityCalculatorTests
{
    [Fact]
    public void Calculate_UsesTrailingMedianAndIqr()
    {
        var start = new DateTime(2026, 1, 1);
        var samples = Enumerable.Range(1, 8)
            .Select(index => new VariabilitySample(
                start.AddDays(index - 1),
                index))
            .ToArray();

        var points = RollingVariabilityCalculator.Calculate(samples, window: 7);

        Assert.Collection(
            points,
            point =>
            {
                Assert.Equal(start.AddDays(6), point.Timestamp);
                Assert.Equal(4, point.Median);
                Assert.Equal(2.5, point.FirstQuartile);
                Assert.Equal(5.5, point.ThirdQuartile);
                Assert.Equal(3, point.InterquartileRange);
                Assert.Equal(7, point.SampleCount);
            },
            point =>
            {
                Assert.Equal(start.AddDays(7), point.Timestamp);
                Assert.Equal(5, point.Median);
                Assert.Equal(3.5, point.FirstQuartile);
                Assert.Equal(6.5, point.ThirdQuartile);
            });
    }

    [Fact]
    public void Calculate_SortsTimestampsStablyIncludingDuplicates()
    {
        var timestamp = new DateTime(2026, 1, 1);
        var samples = new[]
        {
            new VariabilitySample(timestamp.AddDays(1), 9),
            new VariabilitySample(timestamp, 1),
            new VariabilitySample(timestamp, 2),
            new VariabilitySample(timestamp.AddDays(2), 3),
            new VariabilitySample(timestamp.AddDays(3), 4),
            new VariabilitySample(timestamp.AddDays(4), 5),
            new VariabilitySample(timestamp.AddDays(5), 6)
        };

        var point = Assert.Single(
            RollingVariabilityCalculator.Calculate(samples, window: 7));

        Assert.Equal(timestamp.AddDays(5), point.Timestamp);
        Assert.Equal(4, point.Median);
    }

    [Fact]
    public void Calculate_ReturnsNoBandUntilWindowIsFull()
    {
        var samples = Enumerable.Range(0, 6)
            .Select(index => new VariabilitySample(
                new DateTime(2026, 1, 1).AddDays(index),
                index))
            .ToArray();

        Assert.Empty(RollingVariabilityCalculator.Calculate(samples, window: 7));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(30)]
    public void Calculate_RejectsUnsupportedWindows(int window)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            RollingVariabilityCalculator.Calculate([], window));

        Assert.Equal("window", exception.ParamName);
    }
}
