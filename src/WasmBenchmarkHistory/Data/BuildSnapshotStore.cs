namespace WasmBenchmarkHistory.Data;

public sealed class BuildSnapshotStore(IWebHostEnvironment environment)
{
    private readonly string _directory = Path.Combine(environment.ContentRootPath, "DataSets");

    public string[] GetBuildIds() => Directory.Exists(_directory)
        ? Directory.EnumerateFiles(_directory, "*.json.gz")
            .Select(path => Path.GetFileName(path)[..^8])
            .OrderDescending(StringComparer.Ordinal).ToArray()
        : [];

    public Task<BuildSnapshot> LoadAsync(string buildId)
    {
        if (!GetBuildIds().Contains(buildId, StringComparer.Ordinal))
            throw new InvalidDataException("The requested build snapshot is not available.");
        return BuildSnapshotImporter.ReadAsync(Path.Combine(_directory, buildId + ".json.gz"));
    }
}
