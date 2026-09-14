using System.IO.Compression;
using System.Text.Json;

namespace WasmBenchmarkHistory.Data;

public sealed record DirectHistoryArchive(
    int SchemaVersion,
    int Retention,
    DateTimeOffset GeneratedAt,
    DirectHistoryBuild[] Builds,
    DirectHistoryExclusion[] Exclusions);

public sealed record DirectHistoryExclusion(string BuildId, string Reason);

public sealed record DirectHistoryBuild(
    BuildProvenance Build,
    string CaptureSource,
    DateTimeOffset? CapturedAt,
    DirectHistoryLane[] Lanes);

public sealed record DirectHistoryLane(
    string Id,
    string DisplayName,
    string HelixJobId,
    int ExpectedPartitions,
    PartitionCoverage[] Partitions,
    DirectHistoryMeasurement[] Measurements);

public sealed record DirectHistoryMeasurement(
    BenchmarkIdentity Identity,
    double? Mean,
    double? Error,
    double? Variance,
    int? N,
    string? InvalidReason,
    string Partition);

public static class DirectHistoryArchiveBuilder
{
    public static DirectHistoryArchive Create(
        IEnumerable<BuildSnapshot> snapshots,
        int retention,
        DateTimeOffset generatedAt,
        IEnumerable<DirectHistoryExclusion>? additionalExclusions = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(retention, 1);
        var builds = new List<DirectHistoryBuild>();
        var exclusions = new List<DirectHistoryExclusion>();
        foreach (var group in snapshots.GroupBy(snapshot => snapshot.Build.BuildId, StringComparer.Ordinal))
        {
            var candidates = group.Select(Project).ToArray();
            var canonical = candidates[0] with { CapturedAt = null };
            if (candidates.Skip(1).Any(candidate =>
                JsonSerializer.Serialize(candidate with { CapturedAt = null },
                    BuildSnapshotImporter.JsonOptions) !=
                JsonSerializer.Serialize(canonical, BuildSnapshotImporter.JsonOptions)))
                throw new InvalidDataException($"Build {group.Key} has conflicting full snapshots.");
            var selected = candidates.OrderByDescending(candidate => candidate.CapturedAt).First();
            var incomplete = GetIncompleteReason(selected);
            if (incomplete is not null)
                exclusions.Add(new(group.Key, incomplete));
            else
                builds.Add(selected);
        }

        var retained = builds.OrderByDescending(build => Timestamp(build.Build))
            .ThenByDescending(build => BuildNumber(build.Build.BuildId))
            .Take(retention)
            .OrderBy(build => Timestamp(build.Build))
            .ThenBy(build => BuildNumber(build.Build.BuildId))
            .ToArray();
        var pruned = builds.Except(retained).Select(build =>
            new DirectHistoryExclusion(build.Build.BuildId,
                $"Pruned by latest-{retention} complete-build retention.")).ToArray();
        return new DirectHistoryArchive(
            1,
            retention,
            generatedAt,
            retained,
            exclusions.Concat(pruned).Concat(additionalExclusions ?? [])
                .DistinctBy(value => value.BuildId, StringComparer.Ordinal)
                .OrderBy(value => BuildNumber(value.BuildId)).ToArray());
    }

    public static async Task WriteAsync(DirectHistoryArchive archive, string path)
    {
        Validate(archive);
        var json = JsonSerializer.SerializeToUtf8Bytes(archive, BuildSnapshotImporter.JsonOptions);
        SnapshotSafety.ValidateJson(json);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await using var file = File.Create(path);
        await using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        await gzip.WriteAsync(json);
    }

    public static async Task<DirectHistoryArchive> ReadAsync(string path)
    {
        await using var file = File.OpenRead(path);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        var archive = await JsonSerializer.DeserializeAsync<DirectHistoryArchive>(
            gzip, BuildSnapshotImporter.JsonOptions)
            ?? throw new InvalidDataException("Empty direct history archive.");
        Validate(archive);
        return archive;
    }

    public static void Validate(DirectHistoryArchive archive)
    {
        if (archive.SchemaVersion != 1 || archive.Retention <= 0 ||
            archive.Builds.Length > archive.Retention)
            throw new InvalidDataException("Unsupported direct history archive.");
        if (archive.Builds.Select(build => build.Build.BuildId)
            .Distinct(StringComparer.Ordinal).Count() != archive.Builds.Length)
            throw new InvalidDataException("Direct history archive contains duplicate builds.");
        if (!archive.Builds.Select(build => Timestamp(build.Build))
            .SequenceEqual(archive.Builds.Select(build => Timestamp(build.Build)).Order()))
            throw new InvalidDataException("Direct history builds are not ordered by timestamp.");
        foreach (var build in archive.Builds)
        {
            if (string.IsNullOrWhiteSpace(build.Build.BuildId) ||
                !IsSha(build.Build.RuntimeSha) ||
                !IsSha(build.Build.PerformanceSha) ||
                string.IsNullOrWhiteSpace(build.CaptureSource))
                throw new InvalidDataException("Direct history build provenance is incomplete.");
            _ = Timestamp(build.Build);
            if (build.Lanes.Length != BuildComparison.LaneIds.Length ||
                !build.Lanes.Select(lane => lane.Id).Order(StringComparer.Ordinal)
                    .SequenceEqual(BuildComparison.LaneIds.Order(StringComparer.Ordinal)))
                throw new InvalidDataException($"Build {build.Build.BuildId} does not contain all four lanes.");
            foreach (var lane in build.Lanes)
            {
                if (lane.ExpectedPartitions <= 0 ||
                    lane.Partitions.Length != lane.ExpectedPartitions ||
                    lane.Partitions.Select(partition => partition.Name)
                        .Distinct(StringComparer.Ordinal).Count() != lane.Partitions.Length ||
                    lane.Partitions.Any(partition => partition.Reports <= 0) ||
                    lane.Partitions.Sum(partition => partition.Measurements) != lane.Measurements.Length ||
                    lane.Measurements.Any(measurement =>
                        !lane.Partitions.Any(partition => partition.Name == measurement.Partition)) ||
                    lane.Measurements.Any(measurement => measurement.InvalidReason is null &&
                        BuildComparison.InvalidReason(new BenchmarkStatistics(
                            measurement.Mean, null, null, measurement.Error, measurement.N,
                            null, null, measurement.Variance)) is not null))
                    throw new InvalidDataException(
                        $"Build {build.Build.BuildId} lane {lane.Id} is incomplete.");
            }
        }
    }

    private static DirectHistoryBuild Project(BuildSnapshot snapshot)
    {
        BuildComparison.Validate(snapshot);
        return new DirectHistoryBuild(
            snapshot.Build,
            snapshot.CaptureSource,
            snapshot.CapturedAt,
            snapshot.Lanes.Select(lane => new DirectHistoryLane(
                lane.Provenance.Id,
                lane.Provenance.DisplayName,
                lane.Provenance.HelixJobId,
                lane.Provenance.ExpectedPartitions,
                lane.Partitions.OrderBy(partition => PartitionNumber(partition.Name)).ToArray(),
                lane.Measurements.OrderBy(value => value.Identity.DisplayName, StringComparer.Ordinal)
                    .Select(value => new DirectHistoryMeasurement(
                        value.Identity,
                        value.Statistics.Mean,
                        value.Statistics.StandardError,
                        value.Statistics.Variance,
                        value.Statistics.N,
                        value.InvalidReason ?? BuildComparison.InvalidReason(value.Statistics),
                        value.Partition))
                    .ToArray()))
                .OrderBy(lane => Array.IndexOf(BuildComparison.LaneIds, lane.Id))
                .ToArray());
    }

    public static string? GetIncompleteReason(DirectHistoryBuild build)
    {
        foreach (var lane in build.Lanes)
        {
            if (lane.Partitions.Length != lane.ExpectedPartitions)
                return $"{lane.Id} has {lane.Partitions.Length}/{lane.ExpectedPartitions} partitions.";
            var missing = lane.Partitions.Where(partition =>
                partition.Reports <= 0 || partition.Measurements <= 0).ToArray();
            if (missing.Length > 0)
                return $"{lane.Id} has {missing.Length} partitions without usable reports.";
            if (!lane.Measurements.Any(measurement => measurement.InvalidReason is null))
                return $"{lane.Id} has no valid measurements.";
        }
        return null;
    }

    private static DateTimeOffset Timestamp(BuildProvenance build) =>
        DateTimeOffset.TryParse(
            build.SourceDate,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var value)
            ? value.ToUniversalTime()
            : throw new InvalidDataException($"Build {build.BuildId} has an invalid timestamp.");

    private static long BuildNumber(string buildId) =>
        long.TryParse(buildId, out var value) ? value : long.MinValue;

    private static bool IsSha(string value) =>
        value is { Length: 40 } && value.All(char.IsAsciiHexDigit);

    private static int PartitionNumber(string name) =>
        int.TryParse(name.AsSpan("Partition".Length), out var number) ? number : int.MaxValue;
}

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

public sealed record DirectHistoryTrendCell(
    string Status,
    double? Mean,
    double? Error,
    double? Variance,
    int? N,
    double? SpeedupVsMono,
    double? SpeedupVsPrevious);

public sealed record DirectHistoryTrendRow(
    BuildProvenance Build,
    IReadOnlyDictionary<string, DirectHistoryTrendCell> Cells);

public static class DirectHistoryTrend
{
    public static DirectHistoryTrendRow[] Create(DirectHistoryArchive archive, string benchmark)
    {
        var rows = archive.Builds.Select(build =>
        {
            var cells = build.Lanes.ToDictionary(lane => lane.Id, lane =>
            {
                var matches = lane.Measurements.Where(value =>
                    value.Identity.DisplayName == benchmark).ToArray();
                return matches.Length switch
                {
                    0 => new DirectHistoryTrendCell("missing", null, null, null, null, null, null),
                    > 1 => new DirectHistoryTrendCell("duplicate", null, null, null, null, null, null),
                    _ when matches[0].InvalidReason is not null =>
                        new DirectHistoryTrendCell(
                            "invalid", matches[0].Mean, matches[0].Error,
                            matches[0].Variance, matches[0].N, null, null),
                    _ => new DirectHistoryTrendCell(
                        "valid", matches[0].Mean, matches[0].Error,
                        matches[0].Variance, matches[0].N, null, null)
                };
            }, StringComparer.Ordinal);
            var baseline = cells["mono-interpreter"].Mean;
            cells = cells.ToDictionary(pair => pair.Key, pair => pair.Value with
            {
                SpeedupVsMono = pair.Value.Status == "valid" && baseline is > 0
                    ? baseline / pair.Value.Mean : null
            }, StringComparer.Ordinal);
            return new DirectHistoryTrendRow(build.Build, cells);
        }).ToArray();
        for (var index = 0; index < rows.Length; index++)
        {
            var cells = new Dictionary<string, DirectHistoryTrendCell>(StringComparer.Ordinal);
            foreach (var laneId in BuildComparison.LaneIds)
            {
                var current = rows[index].Cells[laneId];
                var previous = index == 0 ? null : rows[index - 1].Cells[laneId];
                cells.Add(laneId, current with
                {
                    SpeedupVsPrevious = current.Status == "valid" &&
                        previous?.Status == "valid" && current.Mean is > 0
                            ? previous.Mean / current.Mean
                            : null
                });
            }
            rows[index] = rows[index] with
            {
                Cells = cells
            };
        }
        return rows;
    }
}

public interface IBenchmarkHistoryProvider
{
    Task<IReadOnlyDictionary<string, IReadOnlySet<string>>> GetAvailabilityAsync();
    Task<BenchmarkHistory?> LoadAsync(string benchmark, RunConfiguration run);
}

public sealed class BundledDirectHistoryProvider(IWebHostEnvironment environment)
    : IBenchmarkHistoryProvider
{
    private readonly string _path = Path.Combine(
        environment.ContentRootPath, "DataSets", "direct-history.json.gz");
    private Task<DirectHistoryArchive?>? _archiveTask;

    public async Task<IReadOnlyDictionary<string, IReadOnlySet<string>>> GetAvailabilityAsync()
    {
        var archive = await LoadArchiveAsync();
        return archive is null
            ? new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
            : DirectSnapshotHistory.GetAvailability(archive.Builds);
    }

    public async Task<BenchmarkHistory?> LoadAsync(string benchmark, RunConfiguration run)
    {
        var archive = await LoadArchiveAsync();
        return archive is null ? null : DirectSnapshotHistory.CreateHistory(benchmark, run, archive.Builds);
    }

    public Task<DirectHistoryArchive?> LoadArchiveAsync() =>
        _archiveTask ??= File.Exists(_path)
            ? ReadArchiveAsync()
            : Task.FromResult<DirectHistoryArchive?>(null);

    private async Task<DirectHistoryArchive?> ReadArchiveAsync() =>
        await DirectHistoryArchiveBuilder.ReadAsync(_path);
}
