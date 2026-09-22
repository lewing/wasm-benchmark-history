namespace WasmBenchmarkHistory.Data;

/// <summary>
/// Loads the benchmark catalog and per-benchmark histories that drive the Trends page.
/// One implementation (<c>BenchmarkHistoryService</c>) fetches live published index/report
/// pages; another (<see cref="PrebuiltBenchmarkHistoryOrchestrator"/>) reads pre-generated
/// static JSON, so the same Razor page can run against either hosting model.
/// </summary>
public interface IBenchmarkHistoryOrchestrator
{
    Task<BenchmarkCatalog> LoadCatalogAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BenchmarkHistory>> LoadHistoriesAsync(
        BenchmarkCatalog catalog,
        string benchmark,
        IReadOnlyList<string> runIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A <see cref="IBenchmarkHistoryOrchestrator"/> composed entirely from
/// <see cref="IBenchmarkHistoryProvider"/> instances (no live index/report page fetching).
/// Used by the standalone WASM app, which reads pre-generated static JSON instead of
/// fetching the (CORS-restricted) published history pages directly from the browser.
/// </summary>
public sealed class PrebuiltBenchmarkHistoryOrchestrator(
    IEnumerable<IBenchmarkHistoryProvider> historyProviders) : IBenchmarkHistoryOrchestrator
{
    private readonly IBenchmarkHistoryProvider[] _historyProviders = historyProviders.ToArray();

    public async Task<BenchmarkCatalog> LoadCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        var runsByBenchmark = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var provider in _historyProviders)
        {
            foreach (var (benchmark, runIds) in await provider.GetAvailabilityAsync())
            {
                if (!runsByBenchmark.TryGetValue(benchmark, out var runs))
                {
                    runs = new HashSet<string>(StringComparer.Ordinal);
                    runsByBenchmark.Add(benchmark, runs);
                }

                runs.UnionWith(runIds);
            }
        }

        return new BenchmarkCatalog(runsByBenchmark.Select(pair =>
            new BenchmarkCatalogEntry(pair.Key, new Dictionary<string, Uri>(), pair.Value)));
    }

    public async Task<IReadOnlyList<BenchmarkHistory>> LoadHistoriesAsync(
        BenchmarkCatalog catalog,
        string benchmark,
        IReadOnlyList<string> runIds,
        CancellationToken cancellationToken = default)
    {
        if (catalog.Find(benchmark) is null)
        {
            throw new BenchmarkDataException(
                BenchmarkDataError.UnknownBenchmark,
                $"Benchmark '{benchmark}' is not present in the loaded indexes.");
        }

        var tasks = runIds.Select(async runId =>
        {
            var run = KnownRunConfigurations.Get(runId);
            var candidates = (await Task.WhenAll(
                    _historyProviders.Select(provider => provider.LoadAsync(benchmark, run))))
                .Where(history => history is not null)
                .Cast<BenchmarkHistory>()
                .ToArray();

            if (candidates.Length == 0)
            {
                throw new BenchmarkDataException(
                    BenchmarkDataError.UnknownBenchmark,
                    $"Benchmark '{benchmark}' is not available for {run.DisplayName}.");
            }

            var merged = candidates[0];
            for (var index = 1; index < candidates.Length; index++)
            {
                var mergeResult = DirectSnapshotHistory.MergeObservations(
                    benchmark, runId, merged.Observations, candidates[index].Observations);
                merged = merged with
                {
                    Observations = mergeResult.Observations,
                    DataConflicts = (merged.DataConflicts ?? [])
                        .Concat(candidates[index].DataConflicts ?? [])
                        .Concat(mergeResult.Conflicts)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray()
                };
            }

            return merged;
        });

        return await Task.WhenAll(tasks);
    }
}
