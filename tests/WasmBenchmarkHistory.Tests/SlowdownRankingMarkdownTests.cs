using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class SlowdownRankingMarkdownTests
{
    [Fact]
    public void Create_ExportsCurrentRankingAsMarkdown()
    {
        var comparison = Comparison();
        var ranking = new BenchmarkRankingResult(
            [new(new("Test", "Bench", "Pipe|Case", "Size=1"), 20, 10, 100)],
            ComparableCount: 2,
            SlowerCount: 1,
            ExcludedCount: 3);

        var markdown = SlowdownRankingMarkdown.Create(
            comparison, ranking, "coreclr-r2r", "mono-aot", " pipe|case ");

        Assert.Contains("# Slowdown ranking", markdown);
        Assert.Contains("**Build:** 123 / 20261004.1 (2026-10-04)", markdown);
        Assert.Contains("**Measured configuration:** CoreCLR R2R", markdown);
        Assert.Contains("**Reference configuration:** Mono AOT", markdown);
        Assert.Contains("**Filter:** pipe\\|case", markdown);
        Assert.Contains("**Rows:** 1 of 1 slower benchmarks", markdown);
        Assert.Contains("**Comparable benchmarks:** 2", markdown);
        Assert.Contains("**Excluded benchmarks:** 3", markdown);
        Assert.Contains("| 1 | Test.Bench.Pipe\\|Case(Size=1) | 20 ns | 10 ns | +100.0% |", markdown);
    }

    [Theory]
    [InlineData("123", "coreclr-r2r", "mono-aot",
        "slowdowns-123-coreclr-r2r-vs-mono-aot.md")]
    [InlineData("../build", "coreclr/r2r", "mono aot",
        "slowdowns-build-coreclr-r2r-vs-mono-aot.md")]
    public void FileName_SanitizesComponents(
        string buildId, string candidateId, string baselineId, string expected) =>
        Assert.Equal(expected, SlowdownRankingMarkdown.FileName(buildId, candidateId, baselineId));

    private static BuildComparisonResult Comparison()
    {
        var build = new BuildProvenance(
            "123", "20261004.1", new('a', 40), new('b', 40), "2026-10-04");
        var lanes = new[]
        {
            Lane("coreclr-r2r", "CoreCLR R2R", build),
            Lane("mono-aot", "Mono AOT", build)
        };
        return new(new(1, build, lanes, []), [], []);
    }

    private static BuildLane Lane(string id, string displayName, BuildProvenance build) =>
        new(new(id, displayName, "", build, "", "", "", "", 1), [], []);
}
