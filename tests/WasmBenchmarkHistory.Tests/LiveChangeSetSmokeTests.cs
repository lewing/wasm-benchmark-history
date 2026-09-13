using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class LiveChangeSetSmokeTests
{
    [Fact]
    [Trait("Category", "Live")]
    public async Task Issue79296_ImportsAndAnalyzesOneHistory()
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
            .LoadAsync(new PerfIssueReference(79296));
        var document = new ChangeSetIssueParser().Parse(payload);

        Assert.Equal(21, document.BenchmarkCount);
        Assert.Equal(10, document.Groups.Count);
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
