using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class BenchmarkRankingTests
{
    [Fact]
    public void Ranking_UsesRelativeSlowdownNotAbsoluteDuration()
    {
        var comparison = Compare(
            [Measurement("SmallButSlow", 30), Measurement("LongButClose", 1100)],
            [Measurement("SmallButSlow", 10), Measurement("LongButClose", 1000)]);

        var ranking = BenchmarkRanking.Create(comparison, "coreclr-r2r", "mono-aot", 100);

        Assert.Equal(["SmallButSlow", "LongButClose"], ranking.Rows.Select(row => row.Identity.Method));
        Assert.Equal(200, ranking.Rows[0].SlowdownPercent);
        Assert.Equal(10, ranking.Rows[1].SlowdownPercent);
        Assert.Equal(2, ranking.ComparableCount);
        Assert.Equal(2, ranking.SlowerCount);
        Assert.Equal(0, ranking.ExcludedCount);
        Assert.Empty(comparison.Common);
    }

    [Fact]
    public void Ranking_ExcludesMissingInvalidAndDuplicatePairsWithoutRequiringOtherLanes()
    {
        var duplicate = Measurement("Duplicate", 10);
        var comparison = Compare(
            [Measurement("Good", 20), Measurement("Missing", 10), duplicate, duplicate,
                Measurement("InvalidMean", 0), Measurement("InvalidN", 10) with
                { Statistics = new(10, null, null, null, 0, null, null) },
                Measurement("ReportedError", 10) with { InvalidReason = "Failed" }],
            [Measurement("Good", 10), duplicate, Measurement("InvalidMean", 10),
                Measurement("InvalidN", 10), Measurement("ReportedError", 5)]);

        var ranking = BenchmarkRanking.Create(comparison, "coreclr-r2r", "mono-aot", 100);

        Assert.Equal("Good", Assert.Single(ranking.Rows).Identity.Method);
        Assert.Equal(1, ranking.ComparableCount);
        Assert.Equal(5, ranking.ExcludedCount);
    }

    [Fact]
    public void Ranking_ExcludesFasterAndEqualButCountsThemAsComparable()
    {
        var comparison = Compare(
            [Measurement("Faster", 5), Measurement("Equal", 10), Measurement("Slower", 20)],
            [Measurement("Faster", 10), Measurement("Equal", 10), Measurement("Slower", 10)]);

        var forward = BenchmarkRanking.Create(comparison, "coreclr-r2r", "mono-aot", 100);
        var reverse = BenchmarkRanking.Create(comparison, "mono-aot", "coreclr-r2r", 100);

        Assert.Equal("Slower", Assert.Single(forward.Rows).Identity.Method);
        Assert.Equal("Faster", Assert.Single(reverse.Rows).Identity.Method);
        Assert.Equal(100, reverse.Rows[0].SlowdownPercent);
        Assert.Equal(3, forward.ComparableCount);
        Assert.Equal(0, forward.ExcludedCount);
    }

    [Fact]
    public void Ranking_LimitsAfterSortingAndBreaksTiesDeterministically()
    {
        var comparison = Compare(
            Enumerable.Range(0, 130).Select(index => Measurement($"Case{index:D3}", 10 + index)).ToArray(),
            Enumerable.Range(0, 130).Select(index => Measurement($"Case{index:D3}", 10)).ToArray());
        var ranking = BenchmarkRanking.Create(comparison, "coreclr-r2r", "mono-aot", 100);

        Assert.Equal(100, ranking.Rows.Length);
        Assert.Equal(129, ranking.SlowerCount);
        Assert.Equal("Case129", ranking.Rows[0].Identity.Method);
        Assert.Equal("Case030", ranking.Rows[^1].Identity.Method);
        Assert.Single(BenchmarkRanking.Create(comparison, "coreclr-r2r", "mono-aot", 1).Rows);

        var tied = Compare(
            [Measurement("Z", 20), Measurement("A", 20)],
            [Measurement("Z", 10), Measurement("A", 10)]);
        Assert.Equal(["A", "Z"], BenchmarkRanking.Create(tied, "coreclr-r2r", "mono-aot", 100)
            .Rows.Select(row => row.Identity.Method));
    }

    [Fact]
    public void Ranking_CountsNonfinitePercentagesAsExcluded()
    {
        var comparison = Compare(
            [Measurement("Overflow", double.MaxValue)], [Measurement("Overflow", double.Epsilon)]);

        var ranking = BenchmarkRanking.Create(comparison, "coreclr-r2r", "mono-aot", 100);

        Assert.Empty(ranking.Rows);
        Assert.Equal(0, ranking.ComparableCount);
        Assert.Equal(1, ranking.ExcludedCount);
    }

    [Theory]
    [InlineData("json")]
    [InlineData(" JsOn ")]
    public void Ranking_FiltersBeforeTopLimitAndScopesAllCounts(string filter)
    {
        var comparison = Compare(
            [Measurement("Other", 1000), Measurement("JsonSlow", 30),
                Measurement("JsonFast", 5), Measurement("JsonMissing", 10)],
            [Measurement("Other", 10), Measurement("JsonSlow", 10), Measurement("JsonFast", 10)]);

        var ranking = BenchmarkRanking.Create(comparison, "coreclr-r2r", "mono-aot", 1, filter);

        Assert.Equal("JsonSlow", Assert.Single(ranking.Rows).Identity.Method);
        Assert.Equal(2, ranking.ComparableCount);
        Assert.Equal(1, ranking.SlowerCount);
        Assert.Equal(1, ranking.ExcludedCount);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("payload: small")]
    [InlineData("my.namespace")]
    [InlineData("customcategory")]
    public void Ranking_FilterMatchesNamesParametersAndCategories(string filter)
    {
        var measurement = Measurement("Deserialize", 30) with
        {
            Identity = new("My.Namespace", "JsonBench", "Deserialize", "Payload: Small"),
            Categories = ["CustomCategory"]
        };
        var baseline = measurement with { Statistics = measurement.Statistics with { Mean = 10 } };
        var ranking = BenchmarkRanking.Create(Compare([measurement], [baseline]),
            "coreclr-r2r", "mono-aot", 100, filter);

        Assert.Equal(measurement.Identity, Assert.Single(ranking.Rows).Identity);
    }

    [Fact]
    public void Ranking_FilterMatchesCanonicalNames()
    {
        var candidate = Measurement("IgnoredStructuredName", 30) with
        {
            Identity = new("", "", "", "", "System.Text.Json.Deserialize(Value: 42)")
        };
        var baseline = candidate with { Statistics = candidate.Statistics with { Mean = 10 } };

        Assert.Single(BenchmarkRanking.Create(Compare([candidate], [baseline]),
            "coreclr-r2r", "mono-aot", 100, "JSON").Rows);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void Ranking_EmptyFilterPreservesUnfilteredResults(string? filter)
    {
        var comparison = Compare([Measurement("Any", 20)], [Measurement("Any", 10)]);
        var ranking = BenchmarkRanking.Create(comparison, "coreclr-r2r", "mono-aot", 100, filter);

        Assert.Single(ranking.Rows);
        Assert.Equal(1, ranking.ComparableCount);
        Assert.Equal(1, ranking.SlowerCount);
        Assert.Equal(0, ranking.ExcludedCount);
    }

    [Fact]
    public void Ranking_UnmatchedFilterHasNoRowsOrUnrelatedExclusions()
    {
        var ranking = BenchmarkRanking.Create(Compare([Measurement("Other", 20)], []),
            "coreclr-r2r", "mono-aot", 100, "json");

        Assert.Empty(ranking.Rows);
        Assert.Equal(0, ranking.ComparableCount);
        Assert.Equal(0, ranking.SlowerCount);
        Assert.Equal(0, ranking.ExcludedCount);
    }

    [Theory]
    [InlineData("coreclr-r2r", "mono-aot", 0)]
    [InlineData("coreclr-r2r", "mono-aot", -1)]
    [InlineData("mono-aot", "mono-aot", 100)]
    [InlineData("unknown", "mono-aot", 100)]
    [InlineData("coreclr-r2r", "unknown", 100)]
    public void Ranking_RejectsInvalidChoices(string candidate, string baseline, int limit) =>
        Assert.ThrowsAny<ArgumentException>(() =>
            BenchmarkRanking.Create(Compare([], []), candidate, baseline, limit));

    [Fact]
    public async Task BundledSnapshot_ProducesTheRequestedTop100ForDefaultPair()
    {
        var snapshot = await BuildSnapshotImporter.ReadAsync(
            Path.Combine(AppContext.BaseDirectory, "DataSets", "3074629.json.gz"));
        var comparison = BuildComparison.Analyze(snapshot);

        var ranking = BenchmarkRanking.Create(comparison, "coreclr-r2r", "mono-aot", 100);

        Assert.Equal(100, ranking.Rows.Length);
        Assert.True(ranking.ComparableCount >= comparison.Common.Length);
        Assert.Equal(ranking.Rows.OrderByDescending(row => row.SlowdownPercent), ranking.Rows);
        Assert.All(ranking.Rows, row =>
        {
            Assert.True(row.CandidateMean > row.BaselineMean);
            Assert.Equal((row.CandidateMean / row.BaselineMean - 1) * 100, row.SlowdownPercent, 7);
        });

        var all = BenchmarkRanking.Create(comparison, "coreclr-r2r", "mono-aot", int.MaxValue);
        var json = BenchmarkRanking.Create(comparison, "coreclr-r2r", "mono-aot", 100, "json");
        Assert.Equal(100, json.Rows.Length);
        Assert.Equal(all.Rows.Where(row => row.Identity.DisplayName.Contains("json",
            StringComparison.OrdinalIgnoreCase)).Take(100), json.Rows);
        Assert.All(json.Rows, row => Assert.Contains("json", row.Identity.DisplayName,
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Create_ReportsBuildWithoutRequestedOptionalLane()
    {
        var build = new BuildProvenance("1", "test", new('a', 40), new('b', 40), "2026-09-29");
        var lanes = BuildComparison.RequiredLaneIds.Select(id => new BuildLane(
            new(id, id, "", build, "", "", "", "", 1), [new("Partition1", "passed", "", 1, 0, 0)], []))
            .ToArray();
        var comparison = BuildComparison.Analyze(new(1, build, lanes, []));

        var exception = Assert.Throws<ArgumentException>(() => BenchmarkRanking.Create(
            comparison, BuildComparison.CoreClrR2RComposite, "coreclr-r2r", 10));

        Assert.Contains("no CoreCLR R2R composite results", exception.Message);
    }

    private static BuildMeasurement Measurement(string method, double mean) =>
        new(new("Test", "Benchmark", method, ""), [], new(mean, null, null, null, 10, null, null),
            "Partition1", "report.json", null);

    private static BuildComparisonResult Compare(BuildMeasurement[] candidate, BuildMeasurement[] baseline)
    {
        var build = new BuildProvenance("1", "test", new('a', 40), new('b', 40), "2026-09-29");
        var lanes = BuildComparison.LaneIds.Select(id =>
        {
            var measurements = id == "coreclr-r2r" ? candidate : id == "mono-aot" ? baseline : [];
            return new BuildLane(new(id, id, "", build, "", "", "", "", 1),
                [new("Partition1", "passed", "", 1, measurements.Length, 0)], measurements);
        }).ToArray();
        return BuildComparison.Analyze(new(1, build, lanes, []));
    }
}
