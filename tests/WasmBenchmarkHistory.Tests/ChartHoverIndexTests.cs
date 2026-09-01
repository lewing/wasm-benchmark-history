using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class ChartHoverIndexTests
{
    [Fact]
    public void RawSelection_SnapsToTimelineAndFindsNearestPointPerRun()
    {
        var start = new DateTime(2026, 9, 1, 10, 0, 0);
        var mono = History(
            KnownRunConfigurations.All[0],
            Observation(KnownRunConfigurations.All[0], start, 10, "r0", "p0"),
            Observation(KnownRunConfigurations.All[0], start.AddMinutes(20), 12, "r2", "p2"));
        var aot = History(
            KnownRunConfigurations.All[1],
            Observation(KnownRunConfigurations.All[1], start.AddMinutes(10), 5, "r1", "p1"));
        var index = new ChartHoverIndex([mono, aot], [], normalized: false);

        var selection = Assert.IsType<ChartHoverSelection>(
            index.SelectNearest(start.AddMinutes(11)));

        Assert.Equal(start.AddMinutes(10), selection.Timestamp);
        Assert.False(selection.HasStrictComparison);
        Assert.Collection(
            selection.Points,
            point =>
            {
                Assert.Equal(start, point.Observation.Timestamp);
                Assert.False(point.IsExactTimestamp);
                Assert.Equal(10, point.DisplayValue);
                Assert.Null(point.StrictRatio);
            },
            point =>
            {
                Assert.Equal(start.AddMinutes(10), point.Observation.Timestamp);
                Assert.True(point.IsExactTimestamp);
                Assert.Equal(5, point.DisplayValue);
                Assert.Null(point.StrictRatio);
            });
    }

    [Fact]
    public void RawSelection_IncludesRatioOnlyForStrictComparison()
    {
        var timestamp = new DateTime(2026, 9, 1, 10, 0, 0);
        var mono = History(
            KnownRunConfigurations.All[0],
            Observation(KnownRunConfigurations.All[0], timestamp, 10, "runtime", "performance"));
        var aot = History(
            KnownRunConfigurations.All[1],
            Observation(KnownRunConfigurations.All[1], timestamp, 4, "runtime", "performance"));
        var histories = new[] { mono, aot };
        var matches = ObservationMatcher.MatchStrict(histories);

        var selection = Assert.IsType<ChartHoverSelection>(
            new ChartHoverIndex(histories, matches, normalized: false).SelectAt(0));

        Assert.True(selection.HasStrictComparison);
        Assert.Equal(1, selection.Points[0].StrictRatio);
        Assert.Equal(.4, selection.Points[1].StrictRatio);
        Assert.Equal(4, selection.Points[1].DisplayValue);
    }

    [Fact]
    public void NormalizedSelection_UsesStrictTimelineAndRatioDisplayValues()
    {
        var timestamp = new DateTime(2026, 9, 1, 10, 0, 0);
        var unmatchedTimestamp = timestamp.AddHours(1);
        var mono = History(
            KnownRunConfigurations.All[0],
            Observation(KnownRunConfigurations.All[0], timestamp, 8, "runtime", "performance"),
            Observation(KnownRunConfigurations.All[0], unmatchedTimestamp, 9, "mono-only", "performance"));
        var aot = History(
            KnownRunConfigurations.All[1],
            Observation(KnownRunConfigurations.All[1], timestamp, 4, "runtime", "performance"),
            Observation(KnownRunConfigurations.All[1], unmatchedTimestamp, 3, "aot-only", "performance"));
        var histories = new[] { mono, aot };
        var index = new ChartHoverIndex(
            histories,
            ObservationMatcher.MatchStrict(histories),
            normalized: true);

        var selection = Assert.IsType<ChartHoverSelection>(
            index.SelectNearest(unmatchedTimestamp));

        Assert.Single(index.Timestamps);
        Assert.Equal(timestamp, selection.Timestamp);
        Assert.Equal(1, selection.Points[0].DisplayValue);
        Assert.Equal(.5, selection.Points[1].DisplayValue);
        Assert.Equal(.5, selection.Points[1].StrictRatio);
        Assert.Equal(0.125, selection.Points[1].Observation.Error);
    }

    [Fact]
    public void RawSelection_ReportsStrictMatchWhenBaselineRatioIsUndefined()
    {
        var timestamp = new DateTime(2026, 9, 1, 10, 0, 0);
        var mono = History(
            KnownRunConfigurations.All[0],
            Observation(KnownRunConfigurations.All[0], timestamp, 0, "runtime", "performance"));
        var aot = History(
            KnownRunConfigurations.All[1],
            Observation(KnownRunConfigurations.All[1], timestamp, 4, "runtime", "performance"));
        var histories = new[] { mono, aot };
        var matches = ObservationMatcher.MatchStrict(histories);

        var selection = Assert.IsType<ChartHoverSelection>(
            new ChartHoverIndex(histories, matches, normalized: false).SelectAt(0));

        Assert.True(selection.HasStrictComparison);
        Assert.All(selection.Points, point => Assert.Null(point.StrictRatio));
        Assert.Empty(new ChartHoverIndex(histories, matches, normalized: true).Timestamps);
    }

    [Fact]
    public void DuplicateStrictMatchesAtSameTimestamp_AreNotCollapsed()
    {
        var timestamp = new DateTime(2026, 9, 1, 10, 0, 0);
        var mono = History(
            KnownRunConfigurations.All[0],
            Observation(KnownRunConfigurations.All[0], timestamp, 10, "runtime-a", "performance-a"),
            Observation(KnownRunConfigurations.All[0], timestamp, 11, "runtime-b", "performance-b"));
        var aot = History(
            KnownRunConfigurations.All[1],
            Observation(KnownRunConfigurations.All[1], timestamp, 5, "runtime-a", "performance-a"),
            Observation(KnownRunConfigurations.All[1], timestamp, 5.5, "runtime-b", "performance-b"));
        var histories = new[] { mono, aot };
        var matches = ObservationMatcher.MatchStrict(histories);

        Assert.Equal(2, matches.Count);
        var rawIndex = new ChartHoverIndex(histories, matches, normalized: false);
        var selection = Assert.IsType<ChartHoverSelection>(rawIndex.SelectAt(0));

        Assert.False(selection.HasStrictComparison);
        Assert.All(selection.Points, point => Assert.Null(point.StrictRatio));
        Assert.Empty(new ChartHoverIndex(histories, matches, normalized: true).Timestamps);
    }

    private static BenchmarkObservation Observation(
        RunConfiguration run,
        DateTime timestamp,
        double value,
        string runtimeSha,
        string performanceSha) =>
        new(
            "Fixture.Benchmark",
            run.Id,
            timestamp,
            value,
            run.Id == "mono-wasm-aot" ? 0.125 : null,
            runtimeSha,
            performanceSha,
            "build-42");

    private static BenchmarkHistory History(
        RunConfiguration run,
        params BenchmarkObservation[] observations) =>
        new("Fixture.Benchmark", run, "build-42", observations);
}
