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

    [Fact]
    public void IntervalComparison_UsesNearestSamplesWithinEachHistory()
    {
        var targetA = new DateTime(2026, 1, 5);
        var targetB = new DateTime(2026, 1, 20);
        var firstRun = IntervalHistory("mono-wasm", [10, 20, 40, 80, 90], [0, 5, 15, 25, 30]);
        var secondRun = IntervalHistory("coreclr-wasm", [100, 50, 25, 10, 5], [0, 5, 15, 25, 30]);

        var first = BenchmarkIntervalComparisonCalculator.Analyze(firstRun, targetA, targetB);
        var second = BenchmarkIntervalComparisonCalculator.Analyze(secondRun, targetA, targetB);

        Assert.Equal(1, first.AtA.Offset.TotalDays);
        Assert.Equal(4, first.AtB.Offset.TotalDays);
        Assert.Equal(20, first.AtA.Observation!.Value);
        Assert.Equal(40, first.AtB.Observation!.Value);
        Assert.Equal(100, first.Analysis!.Points.PercentChange);
        Assert.Equal(50, second.AtA.Observation!.Value);
        Assert.Equal(25, second.AtB.Observation!.Value);
        Assert.Equal(-50, second.Analysis!.Points.PercentChange);
        Assert.Equal("mono-wasm", first.RunId);
        Assert.Equal("coreclr-wasm", second.RunId);
    }

    [Fact]
    public void IntervalComparison_MarksDistantAndEquidistantSamplesUnavailable()
    {
        var history = IntervalHistory("mono-wasm", [10, 20], [0, 10]);

        var distant = BenchmarkIntervalComparisonCalculator.Analyze(
            history,
            new DateTime(2026, 3, 1),
            new DateTime(2026, 1, 1));
        var ambiguous = BenchmarkIntervalComparisonCalculator.Analyze(
            history,
            new DateTime(2026, 1, 6),
            new DateTime(2026, 1, 1));

        Assert.Equal(IntervalSampleStatus.TooDistant, distant.AtA.Status);
        Assert.Null(distant.AtA.Observation);
        Assert.Equal(IntervalSampleStatus.Ambiguous, ambiguous.AtA.Status);
        Assert.Null(ambiguous.Analysis);
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

    private static BenchmarkHistory IntervalHistory(
        string runId,
        double[] values,
        int[] days)
    {
        var run = KnownRunConfigurations.All.Single(candidate => candidate.Id == runId);
        return new BenchmarkHistory(
            "Fixture",
            run,
            "build",
            values.Select((value, index) => new BenchmarkObservation(
                "Fixture",
                runId,
                new DateTime(2026, 1, 1).AddDays(days[index]),
                value,
                .1,
                $"{index + 10:x7}",
                $"{index + 100:x7}",
                $"build-{index}")).ToArray());
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
