namespace WasmBenchmarkHistory.Data;

public sealed record BuildSnapshotDescriptor(
    string BuildId,
    string BuildNumber,
    string CaptureSource,
    DateTimeOffset? CapturedAt,
    string? SourceDate = null)
{
    public string DisplayLabel => CaptureSource == PublishedBuildSnapshot.CaptureSource
        ? $"Published · {SourceDate}"
        : $"{BuildId} / {BuildNumber}";
}

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
            .Where(value => value.Numeric is not null || IsPublishedId(value.Id))
            .OrderByDescending(value => IsPublishedId(value.Id))
            .ThenByDescending(value => IsPublishedId(value.Id) ? value.Id : "", StringComparer.Ordinal)
            .ThenByDescending(value => value.Numeric)
            .ThenByDescending(value => value.Id, StringComparer.Ordinal)
            .Select(value => value.Id).ToArray();

    public static BuildSnapshotDescriptor[] OrderDescriptors(IEnumerable<BuildSnapshotDescriptor> builds) =>
        builds.OrderByDescending(build => DateTime.TryParse(build.SourceDate,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var timestamp) ? timestamp : DateTime.MinValue)
            .ThenByDescending(build => build.BuildId, StringComparer.Ordinal).ToArray();

    private static bool IsPublishedId(string id) =>
        System.Text.RegularExpressions.Regex.IsMatch(id,
            @"^published-\d{14}-[0-9a-f]{40}-[0-9a-f]{40}$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
}
