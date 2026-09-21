using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class BenchmarkHistoryServiceTests
{
    [Fact]
    public async Task PublishedR2RHistoryWinsAndBundledProviderFillsOnlyMissingPoints()
    {
        var cacheDirectory = Path.Combine(
            Path.GetTempPath(),
            "wasm-benchmark-history-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var options = Options.Create(new BenchmarkDataOptions
            {
                CacheDirectory = cacheDirectory
            });
            using var httpClient = new HttpClient(new FixtureHandler());
            var pageClient = new CachedPageClient(
                httpClient,
                new DiskPageCache(options),
                NullLogger<CachedPageClient>.Instance);
            var provider = new StubFallbackProvider();
            var service = new BenchmarkHistoryService(
                pageClient,
                new BenchmarkIndexParser(),
                new BenchmarkHistoryParser(),
                options,
                [provider]);

            var catalog = await service.LoadCatalogAsync();
            var entry = Assert.Single(
                catalog.Entries,
                value => value.Benchmark == StubFallbackProvider.Benchmark);
            Assert.True(entry.Pages.ContainsKey("coreclr-wasm-r2r"));
            Assert.Contains("coreclr-wasm-r2r", entry.DirectRuns);
            Assert.Null(catalog.Find(StubFallbackProvider.FallbackOnlyBenchmark));

            var history = Assert.Single(await service.LoadHistoriesAsync(
                catalog,
                StubFallbackProvider.Benchmark,
                ["coreclr-wasm-r2r"]));

            Assert.Equal(3, history.Observations.Count);
            Assert.Equal(
                2,
                history.Observations.Count(
                    observation => observation.Source == ObservationSource.PublishedHistory));
            var fallback = Assert.Single(
                history.Observations,
                observation => observation.Source == ObservationSource.DirectSnapshot);
            Assert.Equal("fallback-build", fallback.BuildId);
            Assert.Empty(history.DataConflicts ?? []);
        }
        finally
        {
            if (Directory.Exists(cacheDirectory))
            {
                Directory.Delete(cacheDirectory, recursive: true);
            }
        }
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var content = request.RequestUri!.AbsolutePath.EndsWith(
                "AllTestindex.html",
                StringComparison.OrdinalIgnoreCase)
                ? Fixture.Read("index.html")
                : Fixture.Read("history.html");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content),
                RequestMessage = request
            });
        }
    }

    private sealed class StubFallbackProvider : IBenchmarkHistoryProvider
    {
        public const string Benchmark = "Simple.Namespace.Benchmark";
        public const string FallbackOnlyBenchmark = "Fallback.Only.Benchmark";
        private const string RunId = "coreclr-wasm-r2r";

        public Task<IReadOnlyDictionary<string, IReadOnlySet<string>>> GetAvailabilityAsync() =>
            Task.FromResult<IReadOnlyDictionary<string, IReadOnlySet<string>>>(
                new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
                {
                    [Benchmark] = new HashSet<string>([RunId], StringComparer.Ordinal),
                    [FallbackOnlyBenchmark] = new HashSet<string>([RunId], StringComparer.Ordinal)
                });

        public Task<BenchmarkHistory?> LoadAsync(
            string benchmark,
            RunConfiguration run)
        {
            if (benchmark != Benchmark || run.Id != RunId)
            {
                return Task.FromResult<BenchmarkHistory?>(null);
            }

            BenchmarkObservation[] observations =
            [
                new(
                    benchmark,
                    run.Id,
                    new DateTime(2026, 8, 30, 10, 20, 30),
                    10.5,
                    .25,
                    "runtime-a",
                    "performance-a",
                    "primary 'trace",
                    ObservationSource.DirectSnapshot,
                    "equivalent-build"),
                new(
                    benchmark,
                    run.Id,
                    new DateTime(2026, 9, 1, 12, 0, 0),
                    12.25,
                    .2,
                    "runtime-c",
                    "performance-c",
                    "primary 'trace",
                    ObservationSource.DirectSnapshot,
                    "fallback-build")
            ];
            return Task.FromResult<BenchmarkHistory?>(
                new(benchmark, run, "primary 'trace", observations));
        }
    }
}
