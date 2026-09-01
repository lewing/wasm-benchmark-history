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
            entry => entry.Benchmark == "ArrayDeAbstraction.foreach_member_array");
        var selectedRuns = KnownRunConfigurations.All
            .Where(run => benchmark.Pages.ContainsKey(run.Id))
            .Select(run => run.Id)
            .ToArray();

        Assert.Equal(3, selectedRuns.Length);
        var histories = await service.LoadHistoriesAsync(
            catalog,
            benchmark.Benchmark,
            selectedRuns);

        Assert.Equal(3, histories.Count);
        Assert.All(histories, history => Assert.NotEmpty(history.Observations));
        _ = ObservationMatcher.MatchStrict(histories);
    }
}
