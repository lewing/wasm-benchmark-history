using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WasmBenchmarkHistory.Data;

/// <summary>
/// Compact, columnar JSON representation of a single benchmark+run's trend, used to
/// pre-generate static history data (offline) and to read it back (WASM/HTTP or disk).
/// One instance round-trips to/from a single <see cref="BenchmarkHistory"/>.
/// </summary>
public sealed record TrendHistoryDocument(
    string Benchmark,
    string RunId,
    string? TraceName,
    IReadOnlyList<string> Timestamps,
    IReadOnlyList<double> Values,
    IReadOnlyList<double?> Errors,
    IReadOnlyList<string> RuntimeShas,
    IReadOnlyList<string> PerformanceShas)
{
    public static TrendHistoryDocument FromHistory(BenchmarkHistory history) => new(
        history.Benchmark,
        history.Run.Id,
        history.TraceName,
        history.Observations
            .Select(observation => observation.Timestamp.ToString("O", CultureInfo.InvariantCulture))
            .ToArray(),
        history.Observations.Select(observation => observation.Value).ToArray(),
        history.Observations.Select(observation => observation.Error).ToArray(),
        history.Observations.Select(observation => observation.RuntimeSha).ToArray(),
        history.Observations.Select(observation => observation.PerformanceSha).ToArray());

    public BenchmarkHistory ToHistory()
    {
        var run = KnownRunConfigurations.Get(RunId);
        var count = Timestamps.Count;
        if (Values.Count != count || Errors.Count != count
            || RuntimeShas.Count != count || PerformanceShas.Count != count)
        {
            throw new InvalidDataException(
                $"Trend document for '{Benchmark}'/{RunId} has mismatched column lengths.");
        }

        var observations = new BenchmarkObservation[count];
        for (var index = 0; index < count; index++)
        {
            observations[index] = new BenchmarkObservation(
                Benchmark,
                RunId,
                DateTime.Parse(
                    Timestamps[index], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                Values[index],
                Errors[index],
                RuntimeShas[index],
                PerformanceShas[index],
                TraceName);
        }

        return new BenchmarkHistory(Benchmark, run, TraceName, observations);
    }

    public static async Task WriteAsync(TrendHistoryDocument document, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var json = JsonSerializer.SerializeToUtf8Bytes(document, BuildSnapshotImporter.JsonOptions);
        await using var file = File.Create(path);
        await using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        await gzip.WriteAsync(json);
    }

    public static async Task<TrendHistoryDocument> ReadAsync(Stream stream)
    {
        await using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return await JsonSerializer.DeserializeAsync<TrendHistoryDocument>(
            gzip, BuildSnapshotImporter.JsonOptions)
            ?? throw new InvalidDataException("Empty trend history document.");
    }
}

/// <summary>
/// Lists every benchmark for which a pre-generated <see cref="TrendHistoryDocument"/> exists,
/// and which run ids it covers. Small enough to always be loaded up front.
/// </summary>
public sealed record TrendCatalogDocument(IReadOnlyDictionary<string, string[]> Availability)
{
    public static TrendCatalogDocument FromAvailability(
        IReadOnlyDictionary<string, IReadOnlySet<string>> availability) =>
        new(availability.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.Ordinal));

    public IReadOnlyDictionary<string, IReadOnlySet<string>> ToAvailability() =>
        Availability.ToDictionary(
            pair => pair.Key,
            IReadOnlySet<string> (pair) => new HashSet<string>(pair.Value, StringComparer.Ordinal),
            StringComparer.Ordinal);

    public static async Task WriteAsync(TrendCatalogDocument document, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var json = JsonSerializer.SerializeToUtf8Bytes(document, BuildSnapshotImporter.JsonOptions);
        await using var file = File.Create(path);
        await using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        await gzip.WriteAsync(json);
    }

    public static async Task<TrendCatalogDocument> ReadAsync(Stream stream)
    {
        await using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return await JsonSerializer.DeserializeAsync<TrendCatalogDocument>(
            gzip, BuildSnapshotImporter.JsonOptions)
            ?? throw new InvalidDataException("Empty trend catalog document.");
    }
}

/// <summary>
/// Derives a short, stable, filesystem/URL-safe file name for a benchmark identity, since
/// benchmark names contain characters (parentheses, commas, generics) that are not safe
/// to use directly as file names.
/// </summary>
public static class TrendFileNaming
{
    public static string GetFileName(string benchmark)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(benchmark));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
