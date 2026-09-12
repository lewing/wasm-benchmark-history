using System.Text.Json;
using System.Text.Json.Serialization;

namespace WasmBenchmarkHistory.Data;

public sealed record BuildProvenance(
    string BuildId,
    string BuildNumber,
    string RuntimeSha,
    string PerformanceSha,
    string SourceDate);

public sealed record LaneProvenance(
    string Id,
    string DisplayName,
    string HelixJobId,
    BuildProvenance Build,
    string RuntimeVersion,
    string V8Version,
    string WorkloadVersion,
    string Configuration,
    int ExpectedPartitions,
    string? HostSdkVersion = null,
    string? InstallerSdkSha = null,
    string? BenchmarkDotNetVersion = null,
    string? MeasurementConfiguration = null);

public sealed record ImportPartition(string Name, string Status, string Note, string[] Reports);
public sealed record ImportLane(LaneProvenance Provenance, ImportPartition[] Partitions);
public sealed record BuildImportManifest(BuildProvenance Build, ImportLane[] Lanes, string[] Caveats);

public sealed record BenchmarkIdentity(string Namespace, string Type, string Method, string Parameters)
{
    // A structured key avoids delimiter collisions and never incorporates BDN lane job IDs.
    [JsonIgnore]
    public string Key => JsonSerializer.Serialize(new[] { Namespace, Type, Method, Parameters });
    [JsonIgnore]
    public string DisplayName =>
        $"{(Namespace.Length == 0 ? "" : Namespace + ".")}{Type}.{Method}" +
        (Parameters.Length == 0 ? "" : $"({Parameters})");
}

public sealed record BenchmarkStatistics(
    double? Mean,
    double? Median,
    double? StandardDeviation,
    double? StandardError,
    int? N,
    double? Min,
    double? Max,
    double? Variance = null,
    double[]? OriginalValues = null);

public sealed record BuildMeasurement(
    BenchmarkIdentity Identity,
    string[] Categories,
    BenchmarkStatistics Statistics,
    string Partition,
    string Report,
    string? InvalidReason,
    int? MeasurementCount = null,
    string? MeasurementConfiguration = null);

public sealed record PartitionCoverage(
    string Name, string Status, string Note, int Reports, int Measurements, int Unidentified);

public sealed record BuildLane(
    LaneProvenance Provenance,
    PartitionCoverage[] Partitions,
    BuildMeasurement[] Measurements);

public sealed record BuildSnapshot(
    int SchemaVersion, BuildProvenance Build, BuildLane[] Lanes, string[] Caveats);

public sealed record LaneCoverage(
    string LaneId, int Entries, int Unique, int Valid, int Missing, int Invalid, int Duplicates,
    int Unidentified, int Partitions, int ExpectedPartitions);

public sealed record ComparisonCell(string Status, BuildMeasurement[] Measurements)
{
    public BuildMeasurement? ValidMeasurement => Status == "valid" ? Measurements[0] : null;
}

public sealed record BuildComparisonRow(
    BenchmarkIdentity Identity, string[] Categories, IReadOnlyDictionary<string, ComparisonCell> Cells)
{
    public bool IsCommon => Cells.Values.All(cell => cell.ValidMeasurement is not null);
}

public sealed record PairwiseSpeedup(
    string BaselineId, string CandidateId, int Count, double? GeometricMean);

public sealed record BuildComparisonResult(
    BuildSnapshot Snapshot, BuildComparisonRow[] Rows, LaneCoverage[] Coverage)
{
    public BuildComparisonRow[] Common => Rows.Where(row => row.IsCommon).ToArray();
}
