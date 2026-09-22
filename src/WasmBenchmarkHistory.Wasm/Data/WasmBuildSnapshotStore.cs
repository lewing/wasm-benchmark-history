using System.Net.Http.Json;
using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Wasm.Data;

/// <summary>
/// Loads bundled build snapshots over HTTP from wwwroot/DataSets, using the manifest.json
/// generated at build time (see WasmBenchmarkHistory.Wasm.csproj's GenerateDataSetManifest
/// target) since static hosting has no directory listing.
/// </summary>
public sealed class WasmBuildSnapshotStore(HttpClient http) : IBuildSnapshotStore
{
    private Task<string[]>? _buildIdsTask;
    private readonly Dictionary<string, BuildSnapshot> _cache = new(StringComparer.Ordinal);

    private Task<string[]> GetBuildIdsAsync() => _buildIdsTask ??= LoadBuildIdsAsync();

    private async Task<string[]> LoadBuildIdsAsync()
    {
        var ids = await http.GetFromJsonAsync<string[]>("DataSets/manifest.json");
        return BuildSnapshotStoreOrdering.OrderBuildIds(ids ?? []);
    }

    public async Task<BuildSnapshotDescriptor[]> GetDescriptorsAsync()
    {
        var descriptors = new List<BuildSnapshotDescriptor>();
        foreach (var buildId in await GetBuildIdsAsync())
        {
            var snapshot = await LoadAsync(buildId);
            descriptors.Add(new(
                snapshot.Build.BuildId,
                snapshot.Build.BuildNumber,
                snapshot.CaptureSource,
                snapshot.CapturedAt));
        }
        return descriptors.ToArray();
    }

    public async Task<BuildSnapshot> LoadAsync(string buildId)
    {
        if (_cache.TryGetValue(buildId, out var cached))
            return cached;
        var buildIds = await GetBuildIdsAsync();
        if (!buildIds.Contains(buildId, StringComparer.Ordinal))
            throw new InvalidDataException("The requested build snapshot is not available.");
        await using var stream = await http.GetStreamAsync($"DataSets/{buildId}.json.gz");
        var snapshot = await BuildSnapshotImporter.ReadAsync(stream);
        _cache[buildId] = snapshot;
        return snapshot;
    }
}
