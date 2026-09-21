using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class LiveDataSmokeTests
{
    [Fact]
    [Trait("Category", "Live")]
    public async Task PublishedData_LoadsCatalogAndSelectedHistories()
    {
        if (Environment.GetEnvironmentVariable("RUN_LIVE_SMOKE") != "1")
        {
            return;
        }

        var options = Options.Create(new BenchmarkDataOptions
        {
            CacheDirectory = Path.Combine(
                Path.GetTempPath(),
                "wasm-benchmark-history-live-cache"),
            IndexCacheMinutes = 15,
            HistoryCacheMinutes = 15,
            RequestTimeoutSeconds = 60
        });
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(options.Value.RequestTimeoutSeconds)
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("wasm-benchmark-history-smoke/1.0");
        var pageClient = new CachedPageClient(
            httpClient,
            new DiskPageCache(options),
            NullLogger<CachedPageClient>.Instance);
        var service = new BenchmarkHistoryService(
            pageClient,
            new BenchmarkIndexParser(),
            new BenchmarkHistoryParser(),
            options);

        var catalog = await service.LoadCatalogAsync();
        var benchmark = Assert.Single(
            catalog.Entries,
            entry => entry.Benchmark ==
                "BenchmarksGame.FannkuchRedux_2.RunBench(n: 10, expectedSum: 73196)");
        var selectedRuns = KnownRunConfigurations.All.Select(run => run.Id).ToArray();

        Assert.Equal(4, selectedRuns.Length);
        Assert.All(selectedRuns, runId => Assert.True(benchmark.Pages.ContainsKey(runId)));
        var histories = await service.LoadHistoriesAsync(
            catalog,
            benchmark.Benchmark,
            selectedRuns);

        Assert.Equal(4, histories.Count);
        Assert.All(histories, history => Assert.NotEmpty(history.Observations));
        var r2r = Assert.Single(
            histories,
            history => history.Run.Id == "coreclr-wasm-r2r");
        Assert.True(r2r.Observations.Count > 6);
        Assert.All(
            r2r.Observations,
            observation => Assert.Equal(
                ObservationSource.PublishedHistory,
                observation.Source));
        Assert.NotEmpty(ObservationMatcher.MatchStrict(histories));
    }
}
