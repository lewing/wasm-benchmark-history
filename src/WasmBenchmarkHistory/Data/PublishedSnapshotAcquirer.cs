using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WasmBenchmarkHistory.Data;

public sealed class PublishedSnapshotAcquirer(HttpClient httpClient, Action<string> reportProgress)
{
    private static readonly JsonSerializerOptions CacheOptions = new(BuildSnapshotImporter.JsonOptions)
    {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public async Task<BuildSnapshot> AcquireAsync(
        ObservationKey key, string outputPath, string cacheDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!outputPath.EndsWith(".json.gz", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(outputPath) != PublishedBuildSnapshot.Id(key) + ".json.gz")
            throw new ArgumentException(
                $"Output filename must be {PublishedBuildSnapshot.Id(key)}.json.gz.", nameof(outputPath));
        if (key.RuntimeSha.Length != 40 || !key.RuntimeSha.All(char.IsAsciiHexDigit) ||
            key.PerformanceSha.Length != 40 || !key.PerformanceSha.All(char.IsAsciiHexDigit))
            throw new ArgumentException("Full runtime and performance SHAs are required.", nameof(key));

        var parser = new BenchmarkHistoryParser();
        var indexes = new BenchmarkIndexParser();
        var histories = new Dictionary<string, IReadOnlyList<BenchmarkHistory>>(StringComparer.Ordinal);
        foreach (var run in KnownRunConfigurations.Published)
        {
            string index;
            try
            {
                index = await httpClient.GetStringAsync(run.IndexUri!, cancellationToken);
            }
            catch (HttpRequestException exception) when (
                run.IsOptional && exception.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                reportProgress($"{run.Id}: no published index yet.");
                continue;
            }
            var links = indexes.Parse(run.IndexUri!, index);
            var selected = new BenchmarkHistory[links.Count];
            var runCache = Path.Combine(Path.GetFullPath(cacheDirectory), PublishedBuildSnapshot.Id(key), run.Id);
            Directory.CreateDirectory(runCache);
            var completed = 0;
            reportProgress($"{run.Id}: fetching {links.Count} histories (8 concurrent requests).");
            await Parallel.ForEachAsync(Enumerable.Range(0, links.Count),
                new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken },
                async (i, token) =>
                {
                    var link = links[i];
                    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(link.PageUri.AbsoluteUri)));
                    var cachePath = Path.Combine(runCache, hash + ".json");
                    BenchmarkObservation[] observations;
                    if (File.Exists(cachePath))
                    {
                        await using var file = File.OpenRead(cachePath);
                        observations = await JsonSerializer.DeserializeAsync<BenchmarkObservation[]>(
                            file, CacheOptions, token)
                            ?? throw new InvalidDataException($"Empty public snapshot cache for {link.Benchmark}.");
                        if (observations.Any(value => value.Benchmark != link.Benchmark ||
                            value.RunId != run.Id || value.Source != ObservationSource.PublishedHistory ||
                            value.Timestamp != key.Timestamp || value.RuntimeSha != key.RuntimeSha ||
                            value.PerformanceSha != key.PerformanceSha))
                            throw new InvalidDataException($"Public snapshot cache identity mismatch for {link.Benchmark}.");
                    }
                    else
                    {
                        var html = await httpClient.GetStringAsync(link.PageUri, token);
                        var history = parser.Parse(link.Benchmark, run, html, preserveInvalidMeasurements: true);
                        observations = history.Observations.Where(value => value.Timestamp == key.Timestamp &&
                            value.RuntimeSha == key.RuntimeSha && value.PerformanceSha == key.PerformanceSha).ToArray();
                        // Empty matches are retried on the next acquisition, since publication may still be in progress.
                        if (observations.Length > 0)
                        {
                            var temporary = cachePath + ".tmp";
                            await File.WriteAllTextAsync(temporary,
                                JsonSerializer.Serialize(observations, CacheOptions), token);
                            File.Move(temporary, cachePath, overwrite: true);
                        }
                    }
                    selected[i] = new(link.Benchmark, run, null, observations);
                    var count = Interlocked.Increment(ref completed);
                    if (count % 250 == 0 || count == links.Count)
                        reportProgress($"{run.Id}: {count}/{links.Count} histories processed.");
                });
            histories.Add(run.Id, selected);
            reportProgress($"{run.Id}: {selected.Sum(history => history.Observations.Count)} matching observations.");
        }
        var snapshot = PublishedBuildSnapshot.Create(key, histories, DateTimeOffset.UtcNow);
        if (!snapshot.Lanes.Any(lane => lane.Provenance.Id == BuildComparison.CoreClrR2RComposite &&
            lane.Measurements.Any(value => value.InvalidReason is null)))
            throw new InvalidDataException("No usable composite R2R measurements were published for this exact identity.");
        var comparison = BuildComparison.Analyze(snapshot);
        if (comparison.Common.Length == 0)
            throw new InvalidDataException("This identity has no valid all-runtime matches; no snapshot was written.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        var temporaryOutput = outputPath + ".tmp";
        await BuildSnapshotImporter.WriteAsync(snapshot, temporaryOutput);
        File.Move(temporaryOutput, outputPath, overwrite: true);
        reportProgress($"Wrote {outputPath}: {comparison.Rows.Length} identities, " +
            $"{comparison.Common.Length} all-runtime matches.");
        return snapshot;
    }
}
