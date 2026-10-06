using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class SlowdownRankingMarkdownTests
{
    [Fact]
    public async Task Create_ExportsActualFilteredRowsFromBundledSnapshot()
    {
        var snapshot = await BuildSnapshotImporter.ReadAsync(
            Path.Combine(AppContext.BaseDirectory, "DataSets", "3074629.json.gz"));
        var comparison = BuildComparison.Analyze(snapshot);
        var ranking = BenchmarkRanking.Create(
            comparison, "coreclr-r2r", "mono-aot", 3, "json");

        var markdown = SlowdownRankingMarkdown.Create(
            comparison, ranking, "coreclr-r2r", "mono-aot", " json ");

        Assert.Contains("# Slowdown ranking", markdown);
        Assert.Contains("**Build:** 3074629 / 20260913.1 (2026-09-13T14:59:42+00:00)", markdown);
        Assert.Contains("**Measured configuration:** CoreCLR R2R", markdown);
        Assert.Contains("**Reference configuration:** Mono AOT", markdown);
        Assert.Contains("**Filter:** json", markdown);
        Assert.Contains("**Rows:** 3 of 269 slower benchmarks", markdown);
        Assert.Contains("**Comparable benchmarks:** 273", markdown);
        Assert.Contains("**Excluded benchmarks:** 333", markdown);

        var rows = markdown.Split('\n')
            .Where(line => line.StartsWith("| ", StringComparison.Ordinal))
            .Skip(1)
            .ToArray();
        Assert.Equal(3, rows.Length);
        Assert.Equal(
            "| 1 | System.Text.Json.Tests.Utf8JsonReaderCommentsTests.Utf8JsonReaderCommentParsing" +
            "(CommentHandling: Allow, SegmentSize: 0, TestCase: LongMultiLine) | " +
            "7,711,136.11111 ns | 1,010.00973394 ns | +763,371.5% |",
            rows[0]);
        Assert.Equal(
            "| 2 | System.Text.Json.Tests.Utf8JsonReaderCommentsTests.Utf8JsonReaderCommentParsing" +
            "(CommentHandling: Skip, SegmentSize: 0, TestCase: LongMultiLine) | " +
            "7,501,394.44444 ns | 1,019.6026436 ns | +735,617.4% |",
            rows[1]);
        Assert.Equal(
            "| 3 | System.Text.Json.Tests.Perf\\_Ctor.Ctor(Formatted: False, SkipValidation: True) | " +
            "1,115.2564925 ns | 42.9067198434 ns | +2,499.3% |",
            rows[2]);
    }

    [Theory]
    [InlineData("3074629", "coreclr-r2r", "mono-aot",
        "slowdowns-3074629-coreclr-r2r-vs-mono-aot.md")]
    [InlineData("../build", "coreclr/r2r", "mono aot",
        "slowdowns-build-coreclr-r2r-vs-mono-aot.md")]
    public void FileName_SanitizesComponents(
        string buildId, string candidateId, string baselineId, string expected) =>
        Assert.Equal(expected, SlowdownRankingMarkdown.FileName(buildId, candidateId, baselineId));
}
