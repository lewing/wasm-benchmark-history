using Microsoft.Extensions.Options;

namespace WasmBenchmarkHistory.Data;

public sealed class BenchmarkHistoryService(
    CachedPageClient pageClient,
    BenchmarkIndexParser indexParser,
    BenchmarkHistoryParser historyParser,
    IOptions<BenchmarkDataOptions> options)
{
    private readonly BenchmarkDataOptions _options = options.Value;

    public async Task<BenchmarkCatalog> LoadCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        var indexTasks = KnownRunConfigurations.All.Select(async run =>
        {
            var html = await pageClient.GetStringAsync(
                run.IndexUri,
                TimeSpan.FromMinutes(_options.IndexCacheMinutes),
                cancellationToken);
            return (Run: run, Links: indexParser.Parse(run.IndexUri, html));
        });

        var indexes = await Task.WhenAll(indexTasks);
        var pagesByBenchmark = new Dictionary<string, Dictionary<string, Uri>>(StringComparer.Ordinal);

        foreach (var index in indexes)
        {
            foreach (var link in index.Links)
            {
                if (!pagesByBenchmark.TryGetValue(link.Benchmark, out var pages))
                {
                    pages = new Dictionary<string, Uri>(StringComparer.Ordinal);
                    pagesByBenchmark.Add(link.Benchmark, pages);
                }

                pages[index.Run.Id] = link.PageUri;
            }
        }

        return new BenchmarkCatalog(
            pagesByBenchmark.Select(pair => new BenchmarkCatalogEntry(pair.Key, pair.Value)));
    }

    public async Task<IReadOnlyList<BenchmarkHistory>> LoadHistoriesAsync(
        BenchmarkCatalog catalog,
        string benchmark,
        IReadOnlyList<string> runIds,
        CancellationToken cancellationToken = default)
    {
        var entry = catalog.Find(benchmark)
            ?? throw new BenchmarkDataException(
                BenchmarkDataError.UnknownBenchmark,
                $"Benchmark '{benchmark}' is not present in the loaded indexes.");

        var tasks = runIds.Select(async runId =>
        {
            var run = KnownRunConfigurations.Get(runId);
            if (!entry.Pages.TryGetValue(runId, out var pageUri))
            {
                throw new BenchmarkDataException(
                    BenchmarkDataError.UnknownBenchmark,
                    $"Benchmark '{benchmark}' is not available for {run.DisplayName}.");
            }

            var html = await pageClient.GetStringAsync(
                pageUri,
                TimeSpan.FromMinutes(_options.HistoryCacheMinutes),
                cancellationToken);
            return historyParser.Parse(benchmark, run, html);
        });

        return await Task.WhenAll(tasks);
    }
}
