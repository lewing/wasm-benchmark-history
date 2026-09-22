using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Wasm.Data;

/// <summary>
/// Loads the bundled direct-history archive over HTTP from wwwroot/DataSets/direct-history.json.gz.
/// Mirrors the server app's BundledDirectHistoryProvider, but fetches bytes instead of reading
/// them from the local filesystem.
/// </summary>
public sealed class WasmDirectHistoryProvider(HttpClient http)
    : IBenchmarkHistoryProvider, IDirectHistoryArchiveSource
{
    private Task<DirectHistoryArchive?>? _archiveTask;

    public Task<IReadOnlyDictionary<string, IReadOnlySet<string>>> GetAvailabilityAsync() =>
        DirectHistoryProviderHelper.GetAvailabilityAsync(LoadArchiveAsync);

    public Task<BenchmarkHistory?> LoadAsync(string benchmark, RunConfiguration run) =>
        DirectHistoryProviderHelper.LoadAsync(LoadArchiveAsync, benchmark, run);

    public Task<DirectHistoryArchive?> LoadArchiveAsync() => _archiveTask ??= ReadArchiveAsync();

    private async Task<DirectHistoryArchive?> ReadArchiveAsync()
    {
        using var response = await http.GetAsync("DataSets/direct-history.json.gz");
        if (!response.IsSuccessStatusCode)
            return null;
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await DirectHistoryArchiveBuilder.ReadAsync(stream);
    }
}
