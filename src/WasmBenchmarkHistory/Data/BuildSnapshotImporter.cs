using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WasmBenchmarkHistory.Data;

public static class BuildSnapshotImporter
{
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true
    };

    public static async Task<BuildSnapshot> ImportAsync(string manifestPath)
    {
        await using var manifestStream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<BuildImportManifest>(manifestStream, JsonOptions)
            ?? throw new InvalidDataException("Empty import manifest.");
        var root = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        var lanes = new List<BuildLane>();
        foreach (var lane in manifest.Lanes)
        {
            var measurements = new List<BuildMeasurement>();
            var coverage = new List<PartitionCoverage>();
            foreach (var partition in lane.Partitions)
            {
                var startCount = measurements.Count;
                var unidentified = 0;
                foreach (var report in partition.Reports)
                {
                    var path = Path.GetFullPath(report, root);
                    if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                        throw new InvalidDataException("Report paths must remain inside the manifest directory.");
                    await using var stream = File.OpenRead(path);
                    using var document = await JsonDocument.ParseAsync(stream);
                    var parsed = ParseReport(document.RootElement, partition.Name, Path.GetFileName(report),
                        out var rejected);
                    measurements.AddRange(parsed);
                    unidentified += rejected;
                }
                coverage.Add(new PartitionCoverage(partition.Name, partition.Status, partition.Note,
                    partition.Reports.Length, measurements.Count - startCount, unidentified));
            }
            lanes.Add(new BuildLane(lane.Provenance, coverage.ToArray(), measurements.ToArray()));
        }
        var snapshot = new BuildSnapshot(1, manifest.Build, lanes.ToArray(), manifest.Caveats);
        BuildComparison.Validate(snapshot);
        return snapshot;
    }

    public static BuildMeasurement[] ParseReport(
        JsonElement report, string partition, string fileName, out int unidentified)
    {
        if (!report.TryGetProperty("Benchmarks", out var benchmarks) ||
            benchmarks.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"'{fileName}' has no BenchmarkDotNet Benchmarks array.");
        var values = new List<BuildMeasurement>();
        unidentified = 0;
        foreach (var benchmark in benchmarks.EnumerateArray())
        {
            var type = Text(benchmark, "Type");
            var method = Text(benchmark, "Method");
            var parameters = Text(benchmark, "Parameters");
            var ns = benchmark.ValueKind == JsonValueKind.Object &&
                benchmark.TryGetProperty("Namespace", out var namespaceValue) &&
                namespaceValue.ValueKind == JsonValueKind.Null ? "" : Text(benchmark, "Namespace");
            if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(method) ||
                parameters is null || ns is null)
            {
                unidentified++;
                continue;
            }
            var identity = new BenchmarkIdentity(ns, type, method, parameters);
            var stats = benchmark.TryGetProperty("Statistics", out var statistics) &&
                statistics.ValueKind == JsonValueKind.Object ? statistics : default;
            var n = Number(stats, "N");
            var summary = new BenchmarkStatistics(Number(stats, "Mean"), Number(stats, "Median"),
                Number(stats, "StandardDeviation"), Number(stats, "StandardError"),
                n is > 0 and <= int.MaxValue && n == Math.Truncate(n.Value) ? (int)n : null,
                Number(stats, "Min"), Number(stats, "Max"), Number(stats, "Variance"),
                Numbers(stats, "OriginalValues"));
            var categories = benchmark.TryGetProperty("Categories", out var categoryArray) &&
                categoryArray.ValueKind == JsonValueKind.Array
                ? categoryArray.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String)
                    .Select(value => value.GetString()!).Distinct(StringComparer.Ordinal).ToArray()
                : [];
            var invalidReason = BuildComparison.InvalidReason(summary);
            if (HasErrors(report) || HasErrors(benchmark))
                invalidReason = "BenchmarkDotNet reported errors or critical validation failures.";
            if (stats.ValueKind == JsonValueKind.Object &&
                new[] { "StandardDeviation", "StandardError", "Variance" }.Any(property =>
                    stats.TryGetProperty(property, out var value) &&
                    value.ValueKind != JsonValueKind.Null && Number(stats, property) is null))
                invalidReason ??= "Variance statistics are not finite numbers.";
            if (stats.ValueKind == JsonValueKind.Object &&
                stats.TryGetProperty("OriginalValues", out var original) && original.ValueKind != JsonValueKind.Null &&
                (summary.OriginalValues is null || summary.OriginalValues.Length != summary.N))
                invalidReason ??= "Statistics.OriginalValues must be finite and match Statistics.N.";
            var measurementCount = benchmark.TryGetProperty("Measurements", out var measurements) &&
                measurements.ValueKind == JsonValueKind.Array ? (int?)measurements.GetArrayLength() : null;
            values.Add(new BuildMeasurement(identity, categories, summary, partition, fileName, invalidReason,
                measurementCount, MeasurementConfiguration(Text(benchmark, "DisplayInfo"))));
        }
        return values.ToArray();
    }

    public static async Task WriteAsync(BuildSnapshot snapshot, string path)
    {
        BuildComparison.Validate(snapshot);
        await using var file = File.Create(path);
        await using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        await JsonSerializer.SerializeAsync(gzip, snapshot, JsonOptions);
    }

    public static async Task<BuildSnapshot> ReadAsync(string path)
    {
        await using var file = File.OpenRead(path);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        var snapshot = await JsonSerializer.DeserializeAsync<BuildSnapshot>(gzip, JsonOptions)
            ?? throw new InvalidDataException("Empty build snapshot.");
        BuildComparison.Validate(snapshot);
        return snapshot;
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static double? Number(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;

    private static bool HasErrors(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object &&
        new[] { "Errors", "ValidationErrors" }.Any(property =>
        element.TryGetProperty(property, out var value) && IsError(value));

    private static string? MeasurementConfiguration(string? displayInfo)
    {
        if (displayInfo is null)
        return null;
        var parameterStart = displayInfo.IndexOf(" [", StringComparison.Ordinal);
        var job = parameterStart < 0 ? displayInfo : displayInfo[..parameterStart];
        // Full exporters can omit Job. Read only known numeric/enum settings, never copy the label.
        var settings = Regex.Matches(job,
        @"(?<=[(,])\s*(?<setting>(?:EvaluateOverhead=(?:True|False)|" +
        @"IterationTime=\d+(?:\.\d+)?(?:ns|us|ms|s)|" +
        @"(?:MinIterationCount|MaxIterationCount|IterationCount|WarmupCount)=-?\d+|" +
        @"RunStrategy=(?:Throughput|ColdStart|Monitoring)))(?=[,)])",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
        .Select(match => match.Groups["setting"].Value).Order(StringComparer.Ordinal).ToArray();
        return settings.Length == 0 ? null : string.Join("; ", settings);
    }

    private static double[]? Numbers(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var array) ||
        array.ValueKind != JsonValueKind.Array)
        return null;
        var numbers = new List<double>();
        foreach (var value in array.EnumerateArray())
        {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) ||
            !double.IsFinite(number))
            return null;
        numbers.Add(number);
        }
        return numbers.ToArray();
    }

    private static bool IsError(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined or JsonValueKind.False => false,
        JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
        JsonValueKind.Array => value.EnumerateArray().Any(IsError),
        JsonValueKind.Object => !value.TryGetProperty("IsCritical", out var critical) ||
        critical.ValueKind != JsonValueKind.False,
        _ => true
    };
}
