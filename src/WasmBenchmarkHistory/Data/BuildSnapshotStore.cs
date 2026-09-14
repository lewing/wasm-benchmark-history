namespace WasmBenchmarkHistory.Data;

public sealed record BuildSnapshotDescriptor(
    string BuildId,
    string BuildNumber,
    string CaptureSource,
    DateTimeOffset? CapturedAt);

public sealed class BuildSnapshotStore(IWebHostEnvironment environment)
{
    private readonly string _directory = Path.Combine(environment.ContentRootPath, "DataSets");

    public string[] GetBuildIds() => Directory.Exists(_directory)
        ? OrderBuildIds(Directory.EnumerateFiles(_directory, "*.json.gz")
            .Select(path => Path.GetFileName(path)[..^8]))
        : [];

    public static string[] OrderBuildIds(IEnumerable<string> buildIds) =>
        buildIds.Select(id => (Id: id, Numeric: long.TryParse(id, out var value) ? value : (long?)null))
            .Where(value => value.Numeric is not null)
            .OrderByDescending(value => value.Numeric)
            .ThenByDescending(value => value.Id, StringComparer.Ordinal)
            .Select(value => value.Id).ToArray();

    public Task<BuildSnapshot> LoadAsync(string buildId)
    {
        if (!GetBuildIds().Contains(buildId, StringComparer.Ordinal))
            throw new InvalidDataException("The requested build snapshot is not available.");
        return BuildSnapshotImporter.ReadAsync(Path.Combine(_directory, buildId + ".json.gz"));
    }

    public async Task<BuildSnapshot[]> LoadAllAsync()
    {
        var snapshots = new List<BuildSnapshot>();
        foreach (var buildId in GetBuildIds())
            snapshots.Add(await LoadAsync(buildId));
        return snapshots.ToArray();
    }

    public async Task<BuildSnapshotDescriptor[]> GetDescriptorsAsync()
    {
        var descriptors = new List<BuildSnapshotDescriptor>();
        foreach (var buildId in GetBuildIds())
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
}
