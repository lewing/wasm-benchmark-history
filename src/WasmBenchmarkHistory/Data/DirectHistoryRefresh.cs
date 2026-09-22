namespace WasmBenchmarkHistory.Data;

public static class DirectHistoryRefresh
{
    public static async Task<DirectHistoryArchive> BuildAsync(
        IEnumerable<string> snapshotPaths,
        string outputPath,
        int retention,
        DateTimeOffset generatedAt)
    {
        var snapshots = new List<BuildSnapshot>();
        foreach (var path in snapshotPaths.Order(StringComparer.Ordinal))
            snapshots.Add(await BuildSnapshotImporter.ReadAsync(path));
        var archive = DirectHistoryArchiveBuilder.Create(snapshots, retention, generatedAt);
        await DirectHistoryArchiveBuilder.WriteAsync(archive, outputPath);
        return archive;
    }

    public static async Task<DirectHistoryArchive> RefreshAsync(
        IEnumerable<string> builds,
        string fullArchiveDirectory,
        string outputPath,
        int retention)
    {
        fullArchiveDirectory = Path.GetFullPath(fullArchiveDirectory);
        DirectRunAcquirer.ValidateCacheDirectory(fullArchiveDirectory);
        Directory.CreateDirectory(fullArchiveDirectory);
        var acquirer = new DirectRunAcquirer();
        var acquisitionExclusions = new List<DirectHistoryExclusion>();
        var requested = builds.ToArray();
        var automatic = requested.Length == 0;
        if (automatic)
            requested = await acquirer.DiscoverCandidateBuildsAsync(Math.Max(50, retention * 5));
        var completeCandidates = 0;
        foreach (var build in requested)
        {
            var buildUrl = DirectRunAcquirer.NormalizeBuildUrl(build);
            var buildId = System.Web.HttpUtility.ParseQueryString(new Uri(buildUrl).Query)["buildId"]!;
            var snapshotPath = Path.Combine(fullArchiveDirectory, buildId + ".json.gz");
            try
            {
                var snapshot = File.Exists(snapshotPath)
                    ? await BuildSnapshotImporter.ReadAsync(snapshotPath)
                    : await acquirer.AcquireAsync(buildUrl, snapshotPath);
                var projected = DirectHistoryArchiveBuilder.Create(
                    [snapshot], 1, DateTimeOffset.UnixEpoch);
                if (projected.Builds.Length == 1)
                    completeCandidates++;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException
                or HttpRequestException or InvalidOperationException)
            {
                acquisitionExclusions.Add(new(buildId,
                    $"Acquisition failed: {exception.GetType().Name}."));
            }
            if (automatic && completeCandidates >= retention)
                break;
        }
        var snapshots = new List<BuildSnapshot>();
        foreach (var path in Directory.EnumerateFiles(fullArchiveDirectory, "*.json.gz")
            .Where(path => long.TryParse(Path.GetFileName(path)[..^8], out _))
            .Order(StringComparer.Ordinal))
            snapshots.Add(await BuildSnapshotImporter.ReadAsync(path));
        var archive = DirectHistoryArchiveBuilder.Create(
            snapshots, retention, DateTimeOffset.UtcNow, acquisitionExclusions);
        await DirectHistoryArchiveBuilder.WriteAsync(archive, outputPath);
        return archive;
    }
}
