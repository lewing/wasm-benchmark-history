using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class LiveChangeSetSmokeTests
{
    [Theory]
    [InlineData(79296, 21, 10)]
    [InlineData(83138, 25, 8)]
    [Trait("Category", "Live")]
    public async Task Issue_ImportsAndAnalyzesOneHistory(int issueNumber, int benchmarkCount, int groupCount)
    {
        if (Environment.GetEnvironmentVariable("RUN_LIVE_CHANGE_SET_SMOKE") != "1")
        {
            return;
        }

        var githubOptions = Options.Create(new GitHubIssueOptions
        {
            Token = Environment.GetEnvironmentVariable("GITHUB_TOKEN"),
            RequestTimeoutSeconds = 60
        });
        using var github = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false
        })
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
        github.DefaultRequestHeaders.UserAgent.ParseAdd("wasm-benchmark-history-smoke/1.0");
        github.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        var payload = await new PerfAutofilingIssueClient(github, githubOptions)
            .LoadAsync(new PerfIssueReference(issueNumber));
        var document = new ChangeSetIssueParser().Parse(payload);

        Assert.Equal(benchmarkCount, document.BenchmarkCount);
        Assert.Equal(groupCount, document.Groups.Count);
        var group = document.Groups[0];
        var row = group.Rows[0];

        var dataOptions = Options.Create(new BenchmarkDataOptions
        {
            CacheDirectory = Path.Combine(
                Path.GetTempPath(),
                "wasm-benchmark-history-change-set-smoke"),
            IndexCacheMinutes = 15,
            HistoryCacheMinutes = 15,
            RequestTimeoutSeconds = 60
        });
        using var reports = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
        reports.DefaultRequestHeaders.UserAgent.ParseAdd("wasm-benchmark-history-smoke/1.0");
        var pageClient = new CachedPageClient(
            reports,
            new DiskPageCache(dataOptions),
            NullLogger<CachedPageClient>.Instance);
        var service = new BenchmarkHistoryService(
            pageClient,
            new BenchmarkIndexParser(),
            new BenchmarkHistoryParser(),
            dataOptions);
        var history = await service.LoadImportedHistoryAsync(row.HistoryUri);
        var analysis = ChangeSetHistoryAnalyzer.Analyze(
            history,
            group.Run.BaselineRuntimeSha,
            group.Run.CompareRuntimeSha,
            document.Metadata.PerformanceSha);
        var preview = ChangeSetHistoryAnalyzer.CreatePreview(
            history,
            group.Run.BaselineRuntimeSha,
            group.Run.CompareRuntimeSha,
            document.Metadata.PerformanceSha);

        Assert.Equal(row.Benchmark, history.Benchmark);
        Assert.NotEmpty(analysis.BenchmarkViewUri);
        Assert.Single(preview.Points, point => point.IsBaseline);
        Assert.Single(preview.Points, point => point.IsCompare);
    }
}
