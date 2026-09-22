namespace WasmBenchmarkHistory.Data;

public sealed record BuildSnapshotDescriptor(
    string BuildId,
    string BuildNumber,
    string CaptureSource,
    DateTimeOffset? CapturedAt);

/// <summary>
/// Loads bundled build snapshots by id. Implementations differ by hosting model: the server
/// app enumerates the local DataSets directory, while the WebAssembly app fetches a manifest
/// and gzip payloads over HTTP.
/// </summary>
public interface IBuildSnapshotStore
{
    Task<BuildSnapshotDescriptor[]> GetDescriptorsAsync();
    Task<BuildSnapshot> LoadAsync(string buildId);
}

public static class BuildSnapshotStoreOrdering
{
    public static string[] OrderBuildIds(IEnumerable<string> buildIds) =>
        buildIds.Select(id => (Id: id, Numeric: long.TryParse(id, out var value) ? value : (long?)null))
            .Where(value => value.Numeric is not null)
            .OrderByDescending(value => value.Numeric)
            .ThenByDescending(value => value.Id, StringComparer.Ordinal)
            .Select(value => value.Id).ToArray();
}
