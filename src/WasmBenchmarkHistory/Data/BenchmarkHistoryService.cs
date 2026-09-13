using Microsoft.Extensions.Options;

namespace WasmBenchmarkHistory.Data;

public sealed class BenchmarkHistoryService(
    CachedPageClient pageClient,
    BenchmarkIndexParser indexParser,
    BenchmarkHistoryParser historyParser,
    IOptions<BenchmarkDataOptions> options,
    BuildSnapshotStore? snapshotStore = null)
{
    private readonly BenchmarkDataOptions _options = options.Value;
    private Task<BuildSnapshot[]>? _snapshotTask;

    public async Task<BenchmarkCatalog> LoadCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        var indexTasks = KnownRunConfigurations.Published.Select(async run =>
        {
            var html = await pageClient.GetStringAsync(
                run.IndexUri!,
                TimeSpan.FromMinutes(_options.IndexCacheMinutes),
                cancellationToken);
            return (Run: run, Links: indexParser.Parse(run.IndexUri!, html));
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

        var snapshots = await LoadSnapshotsAsync();
        var directAvailability = DirectSnapshotHistory.GetAvailability(snapshots);
        foreach (var direct in directAvailability)
        {
            if (!pagesByBenchmark.ContainsKey(direct.Key))
                pagesByBenchmark.Add(direct.Key, new Dictionary<string, Uri>(StringComparer.Ordinal));
        }

        return new BenchmarkCatalog(
            pagesByBenchmark.Select(pair => new BenchmarkCatalogEntry(
                pair.Key,
                pair.Value,
                directAvailability.GetValueOrDefault(pair.Key))));
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

        var snapshots = await LoadSnapshotsAsync();
        var tasks = runIds.Select(async runId =>
        {
            var run = KnownRunConfigurations.Get(runId);
            BenchmarkHistory? published = null;
            if (entry.Pages.TryGetValue(runId, out var pageUri))
            {
                var html = await pageClient.GetStringAsync(
                    pageUri,
                    TimeSpan.FromMinutes(_options.HistoryCacheMinutes),
                    cancellationToken);
                published = historyParser.Parse(benchmark, run, html);
            }
            var direct = entry.DirectRuns.Contains(runId)
                ? DirectSnapshotHistory.CreateHistory(benchmark, run, snapshots)
                : null;
            if (published is null && direct is null)
            {
                throw new BenchmarkDataException(
                    BenchmarkDataError.UnknownBenchmark,
                    $"Benchmark '{benchmark}' is not available for {run.DisplayName}.");
            }
            if (published is null)
                return direct!;
            if (direct is null)
                return published;
            var merged = DirectSnapshotHistory.MergeObservations(
                benchmark, runId, published.Observations, direct.Observations);
            return published with
            {
                Observations = merged.Observations,
                DataConflicts = (direct.DataConflicts ?? []).Concat(merged.Conflicts)
                    .Distinct(StringComparer.Ordinal).ToArray()
            };
        });

        return await Task.WhenAll(tasks);
    }

    public async Task<BenchmarkHistory> LoadImportedHistoryAsync(
        Uri historyUri,
        CancellationToken cancellationToken = default)
    {
        var target = ImportedHistoryTarget.Parse(historyUri);
        var html = await pageClient.GetStringAsync(
            target.HistoryUri,
            TimeSpan.FromMinutes(_options.HistoryCacheMinutes),
            cancellationToken);
        return historyParser.Parse(target.Benchmark, target.Run, html);
    }

    private Task<BuildSnapshot[]> LoadSnapshotsAsync() =>
        _snapshotTask ??= snapshotStore?.LoadAllAsync() ?? Task.FromResult(Array.Empty<BuildSnapshot>());
}
