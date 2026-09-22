namespace WasmBenchmarkHistory.Data;

public sealed class BundledDirectHistoryProvider(IWebHostEnvironment environment)
    : IBenchmarkHistoryProvider, IDirectHistoryArchiveSource
{
    private readonly string _path = Path.Combine(
        environment.ContentRootPath, "DataSets", "direct-history.json.gz");
    private Task<DirectHistoryArchive?>? _archiveTask;

    public Task<IReadOnlyDictionary<string, IReadOnlySet<string>>> GetAvailabilityAsync() =>
        DirectHistoryProviderHelper.GetAvailabilityAsync(LoadArchiveAsync);

    public Task<BenchmarkHistory?> LoadAsync(string benchmark, RunConfiguration run) =>
        DirectHistoryProviderHelper.LoadAsync(LoadArchiveAsync, benchmark, run);

    public Task<DirectHistoryArchive?> LoadArchiveAsync() =>
        _archiveTask ??= File.Exists(_path)
            ? ReadArchiveAsync()
            : Task.FromResult<DirectHistoryArchive?>(null);

    private async Task<DirectHistoryArchive?> ReadArchiveAsync() =>
        await DirectHistoryArchiveBuilder.ReadAsync(_path);
}
