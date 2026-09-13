using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class ChangeSetIssueTests
{
    [Theory]
    [InlineData("79296")]
    [InlineData("dotnet/perf-autofiling-issues#79296")]
    [InlineData("https://github.com/dotnet/perf-autofiling-issues/issues/79296")]
    public void IssueReference_NormalizesSupportedInputs(string input)
    {
        var issue = PerfIssueReference.Parse(input);

        Assert.Equal(79296, issue.Number);
        Assert.Equal(
            "https://api.github.com/repos/dotnet/perf-autofiling-issues/issues/79296",
            issue.ApiUri.AbsoluteUri);
    }

    [Theory]
    [InlineData("dotnet/runtime#1", PerfIssueError.UnsupportedRepository)]
    [InlineData("https://evil.example/dotnet/perf-autofiling-issues/issues/1", PerfIssueError.InvalidInput)]
    [InlineData("https://github.com/dotnet/perf-autofiling-issues/issues/1?redirect=evil", PerfIssueError.InvalidInput)]
    [InlineData("0", PerfIssueError.InvalidInput)]
    [InlineData(" ", PerfIssueError.InvalidInput)]
    public void IssueReference_RejectsUnsupportedInputs(
        string input,
        PerfIssueError expected)
    {
        var exception = Assert.Throws<PerfIssueException>(
            () => PerfIssueReference.Parse(input));

        Assert.Equal(expected, exception.Error);
    }

    [Fact]
    public void Parser_ReadsMetadataGroupsEntitiesLinksAndRepro()
    {
        var document = ParseFixture();

        Assert.Equal("dotnetruntime", document.Metadata.Repository);
        Assert.Equal("refs/heads/main", document.Metadata.Branch);
        Assert.False(document.Metadata.IsRegression);
        Assert.Equal(new DateTime(2026, 9, 7, 9, 35, 50), document.Metadata.RegressionDate);
        Assert.Equal(2, document.Groups.Count);
        Assert.Equal(3, document.BenchmarkCount);

        var group = document.Groups[0];
        Assert.Equal("Example.Generic<String>", group.Name);
        Assert.Equal(ChangeDirection.Improvement, group.Direction);
        Assert.Contains("Example.Generic<String>*", group.ReproCommand);
        Assert.Equal(
            "Example.Generic(String).Run(Path: \"a/b+c\", Items: [1,2])",
            group.Rows[0].Benchmark);
        Assert.Equal("Run/Case", group.Rows[0].DisplayName);
        Assert.Equal("100.0 ns", group.Rows[0].ReportedBaseline);
        Assert.Equal(.80, group.Rows[0].Ratio);
        Assert.Equal(.02, group.Rows[0].TestQuality);
        Assert.False(group.Rows[0].EdgeDetector);
        Assert.NotNull(group.Rows[0].SourceUri);
        Assert.NotNull(group.Rows[0].ReportUri);
        Assert.Equal(
            "Example.Generic(String).Operator<T>(Value: \"&lt;|y\")",
            group.Rows[1].Benchmark);

        Assert.Equal("Example.Raw/Slash", document.Groups[1].Name);
        Assert.Equal(
            "Example.Raw/Slash.Case",
            document.Groups[1].Rows[0].Benchmark);
        Assert.NotEqual(
            document.Groups[0].Run.CompareRuntimeSha,
            document.Groups[1].Run.CompareRuntimeSha);
    }

    [Fact]
    public void Parser_RejectsUnsafeExpectedLinks()
    {
        var body = Fixture.Read("autofile-issue.md").Replace(
            "https://github.com/dotnet/performance/blob/",
            "https://example.com/dotnet/performance/blob/",
            StringComparison.Ordinal);
        var payload = Payload(body, []);

        var exception = Assert.Throws<PerfIssueException>(
            () => new ChangeSetIssueParser().Parse(payload));

        Assert.Equal(PerfIssueError.Schema, exception.Error);
        Assert.Contains("source link", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parser_RejectsRuntimeDiffThatDoesNotMatchDisplayedShas()
    {
        var body = Fixture.Read("autofile-issue.md").Replace(
            "1111111111111111111111111111111111111111...2222222222222222222222222222222222222222",
            "1111111111111111111111111111111111111111...4444444444444444444444444444444444444444",
            StringComparison.Ordinal);

        var exception = Assert.Throws<PerfIssueException>(
            () => new ChangeSetIssueParser().Parse(Payload(body, [])));

        Assert.Contains("did not match", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parser_SanitizesExternalTriageToPlainSummaryAndSafeLinks()
    {
        var triage = new PerfIssueComment(
            "automation",
            DateTimeOffset.Parse("2026-09-10T13:30:21Z"),
            Fixture.Read("autofile-triage.md"));
        var document = new ChangeSetIssueParser().Parse(
            Payload(Fixture.Read("autofile-issue.md"), [triage]));

        var summary = Assert.IsType<ExternalTriageSummary>(document.ExternalTriage);
        Assert.DoesNotContain("<script", summary.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("alert(\"not markup\")", summary.Summary);
        var link = Assert.Single(summary.Links);
        Assert.Equal("github.com", link.Uri.Host);
        Assert.Equal("runtime change", link.Text);
    }

    [Theory]
    [InlineData(.80, .02, false, ReportedSignalQuality.StrongReportedSignal)]
    [InlineData(.95, .02, false, ReportedSignalQuality.Review)]
    [InlineData(.80, .18, false, ReportedSignalQuality.HighVarianceOrNoiseRisk)]
    [InlineData(.80, .02, true, ReportedSignalQuality.HighVarianceOrNoiseRisk)]
    public void SignalHeuristic_UsesDocumentedReportedFacts(
        double ratio,
        double quality,
        bool edge,
        ReportedSignalQuality expected)
    {
        var benchmark = ParseFixture().Groups[0].Rows[0] with
        {
            Ratio = ratio,
            TestQuality = quality,
            EdgeDetector = edge
        };

        Assert.Equal(expected, ChangeSetSignalHeuristic.Classify(benchmark));
    }

    [Fact]
    public void PageState_RoundTripsIssueFiltersAndSort()
    {
        var state = new ChangeSetPageState(
            79296,
            "Generic<String>(path: \"a/b\")",
            ChangeSetSort.ReportedQuality,
            ChangeSetQualityFilter.NoiseRisk);

        var parsed = ChangeSetPageStateCodec.Parse(
            ChangeSetPageStateCodec.ToRelativeUri(state));

        Assert.Equal(state, parsed);
    }

    private static ChangeSetDocument ParseFixture() =>
        new ChangeSetIssueParser().Parse(
            Payload(Fixture.Read("autofile-issue.md"), []));

    private static PerfIssuePayload Payload(
        string body,
        IReadOnlyList<PerfIssueComment> comments) =>
        new(
            79296,
            "Fixture change set",
            "open",
            new Uri("https://github.com/dotnet/perf-autofiling-issues/issues/79296"),
            body,
            ["arch-wasm", "perf-improvement"],
            comments);
}
