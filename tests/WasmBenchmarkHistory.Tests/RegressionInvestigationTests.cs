using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class RegressionInvestigationTests
{
    [Fact]
    public void Analyze_OrdersReversePinsChronologically()
    {
        var history = History(10, 12, 18);
        var baselineA = Pin(history.Observations[2]);
        var comparisonB = Pin(history.Observations[0]);

        var result = Assert.IsType<RegressionInvestigationResult>(
            RegressionInvestigation.Analyze(history, baselineA, comparisonB, windowRadius: 0));

        Assert.True(result.Points.PinsWereReordered);
        Assert.Equal(InvestigationPinSlot.ComparisonB, result.Points.OlderSlot);
        Assert.Equal(10, result.Points.Older.Value);
        Assert.Equal(18, result.Points.Newer.Value);
        Assert.Equal(8, result.Points.AbsoluteDelta);
        Assert.Equal(80, result.Points.PercentChange);
        Assert.Equal(1.8, result.Points.Ratio);
    }

    [Fact]
    public void Analyze_HandlesZeroOlderValueWithoutInfinity()
    {
        var history = History(0, 5);

        var result = Assert.IsType<RegressionInvestigationResult>(
            RegressionInvestigation.Analyze(
                history,
                Pin(history.Observations[0]),
                Pin(history.Observations[1]),
                windowRadius: 0));

        Assert.Equal(5, result.Points.AbsoluteDelta);
        Assert.Null(result.Points.PercentChange);
        Assert.Null(result.Points.Ratio);
        Assert.Null(result.Windows.PercentChange);
        Assert.Null(result.Windows.Ratio);
    }

    [Fact]
    public void SummarizeWindow_ComputesMedianAndInterpolatedIqr()
    {
        var history = History(1, 2, 3, 4, 5, 6, 100);

        var summary = Assert.IsType<RobustWindowSummary>(
            RegressionInvestigation.SummarizeWindow(
                history,
                Pin(history.Observations[3]),
                windowRadius: 3));

        Assert.Equal(7, summary.Count);
        Assert.Equal(4, summary.Median);
        Assert.Equal(2.5, summary.FirstQuartile);
        Assert.Equal(5.5, summary.ThirdQuartile);
        Assert.Equal(3, summary.InterquartileRange);
    }

    [Fact]
    public void Resolve_RejectsDuplicateObservationIdentity()
    {
        var first = Observation(0, 10, "aaaaaaa", "bbbbbbb");
        var duplicate = first with { Value = 11 };
        var history = new BenchmarkHistory(
            "Fixture",
            KnownRunConfigurations.All[0],
            "build",
            [first, duplicate]);

        Assert.Null(RegressionInvestigation.Resolve(history, Pin(first)));
    }

    [Theory]
    [InlineData("abcdef1", "1234567", true)]
    [InlineData("not-a-sha", "1234567", false)]
    [InlineData("abcdef1", "same-sha", false)]
    public void CompareLinks_ValidateShas(string older, string newer, bool expected)
    {
        Assert.Equal(expected, GitHubCompareLinkBuilder.Runtime(older, newer) is not null);
    }

    private static BenchmarkHistory History(params double[] values)
    {
        var observations = values
            .Select((value, index) => Observation(
                index,
                value,
                $"{index + 10:x7}",
                $"{index + 100:x7}"))
            .ToArray();
        return new BenchmarkHistory(
            "Fixture",
            KnownRunConfigurations.All[0],
            "build",
            observations);
    }

    private static BenchmarkObservation Observation(
        int index,
        double value,
        string runtimeSha,
        string performanceSha) =>
        new(
            "Fixture",
            KnownRunConfigurations.All[0].Id,
            new DateTime(2026, 1, 1).AddDays(index),
            value,
            .1,
            runtimeSha,
            performanceSha,
            $"build-{index}");

    private static InvestigationPin Pin(BenchmarkObservation observation) =>
        new(observation.RunId, ObservationIdentity.From(observation));
}
