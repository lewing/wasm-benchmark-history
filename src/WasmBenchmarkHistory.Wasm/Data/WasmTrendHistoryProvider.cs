using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Wasm.Data;

/// <summary>
/// Loads pre-generated static trend data over HTTP from wwwroot/TrendData, produced offline
/// by the WasmBenchmarkHistory.TrendBuilder tool. Feeds a <see cref="PrebuiltBenchmarkHistoryOrchestrator"/>
/// so the Trends page can run without live (CORS-restricted) fetches of the published report pages.
/// </summary>
public sealed class WasmTrendHistoryProvider(HttpClient http) : IBenchmarkHistoryProvider
{
    private Task<TrendCatalogDocument?>? _catalogTask;
    private readonly Dictionary<string, Task<TrendHistoryDocument?>> _historyTasks = [];

    public async Task<IReadOnlyDictionary<string, IReadOnlySet<string>>> GetAvailabilityAsync()
    {
        var catalog = await LoadCatalogAsync();
        return catalog?.ToAvailability()
            ?? new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
    }

    public async Task<BenchmarkHistory?> LoadAsync(string benchmark, RunConfiguration run)
    {
        var document = await LoadHistoryDocumentAsync(benchmark, run);
        return document?.ToHistory();
    }

    private Task<TrendCatalogDocument?> LoadCatalogAsync() =>
        _catalogTask ??= ReadCatalogAsync();

    private async Task<TrendCatalogDocument?> ReadCatalogAsync()
    {
        using var response = await http.GetAsync("TrendData/catalog.json.gz");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        return await TrendCatalogDocument.ReadAsync(stream);
    }

    private Task<TrendHistoryDocument?> LoadHistoryDocumentAsync(string benchmark, RunConfiguration run)
    {
        var key = $"{run.Id}/{benchmark}";
        if (!_historyTasks.TryGetValue(key, out var task))
        {
            task = ReadHistoryDocumentAsync(benchmark, run);
            _historyTasks[key] = task;
        }

        return task;
    }

    private async Task<TrendHistoryDocument?> ReadHistoryDocumentAsync(string benchmark, RunConfiguration run)
    {
        var fileName = TrendFileNaming.GetFileName(benchmark);
        using var response = await http.GetAsync($"TrendData/{run.Id}/{fileName}.json.gz");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        return await TrendHistoryDocument.ReadAsync(stream);
    }
}
