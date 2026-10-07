using System.Globalization;

namespace WasmBenchmarkHistory.Data;

public static class PublishedBuildSnapshot
{
    public const string CaptureSource = "Published-history snapshot";

    public static string Id(ObservationKey key) =>
        $"published-{key.Timestamp.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}-{key.RuntimeSha}-{key.PerformanceSha}";

    public static BuildSnapshot Create(
        ObservationKey key,
        IReadOnlyDictionary<string, IReadOnlyList<BenchmarkHistory>> histories,
        DateTimeOffset capturedAt)
    {
        var build = new BuildProvenance(
            Id(key), "unavailable", key.RuntimeSha, key.PerformanceSha,
            key.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        var lanes = KnownRunConfigurations.All.Where(run => histories.ContainsKey(run.Id)).Select(run =>
        {
            var measurements = histories[run.Id].SelectMany(history =>
            {
                if (history.Run.Id != run.Id)
                    throw new InvalidDataException($"History run disagrees with '{run.Id}'.");
                return history.Observations.Where(value =>
                    value.Timestamp == key.Timestamp &&
                    value.RuntimeSha == key.RuntimeSha &&
                    value.PerformanceSha == key.PerformanceSha).Select(value =>
                {
                    if (value.Source != ObservationSource.PublishedHistory ||
                        value.RunId != run.Id || value.Benchmark != history.Benchmark)
                        throw new InvalidDataException("Only unmodified published observations can be projected.");
                    var stats = new BenchmarkStatistics(
                        double.IsFinite(value.Value) ? value.Value : null, null, null, null, null, null, null);
                    var reason = BuildComparison.InvalidReason(stats, requireSampleCount: false);
                    if (value.Error is { } error && (!double.IsFinite(error) || error < 0))
                        reason ??= "Published error must be finite and non-negative.";
                    return new BuildMeasurement(
                        new("", "", "", "", history.Benchmark), [], stats, "unavailable",
                        new Uri(run.IndexUri!, Uri.EscapeDataString(history.Benchmark) + ".html").AbsoluteUri,
                        reason, PublishedError: value.Error is { } finiteError && double.IsFinite(finiteError)
                            ? finiteError : null);
                });
            }).OrderBy(value => value.Identity.DisplayName, StringComparer.Ordinal).ToArray();
            return new BuildLane(
                new(DirectSnapshotHistory.LaneId(run.Id), run.DisplayName, "unavailable", build,
                    "unavailable", "unavailable", "unavailable",
                    Uri.UnescapeDataString(run.IndexUri!.AbsolutePath), 0),
                [], measurements);
        }).ToArray();
        var snapshot = new BuildSnapshot(2, build, lanes,
        [
            "Public history values are selected by exact timestamp, runtime SHA, and performance SHA, not by nearest date or latest value.",
            "Build ID/number, Helix jobs, partition inventory, machine details, sample counts, and full BenchmarkDotNet statistics are unavailable.",
            "Published values and error bars may be rounded. Error-bar semantics are not asserted to be standard error.",
            "Coverage describes the fetched public indexes, not verified completeness of the original build. Missing and duplicate observations remain explicit."
        ], CaptureSource, capturedAt, key);
        BuildComparison.Validate(snapshot);
        return snapshot;
    }

    public static void Validate(BuildSnapshot snapshot)
    {
        var key = snapshot.PublishedIdentity
            ?? throw new InvalidDataException("A public snapshot requires its exact published identity.");
        if (key.Timestamp.Kind != DateTimeKind.Unspecified ||
            snapshot.Build.BuildId != Id(key) ||
            snapshot.Build.RuntimeSha != key.RuntimeSha ||
            snapshot.Build.PerformanceSha != key.PerformanceSha ||
            snapshot.Build.SourceDate != key.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ||
            snapshot.Build.BuildNumber != "unavailable" ||
            snapshot.CaptureSource != CaptureSource)
            throw new InvalidDataException("Published snapshot provenance disagrees with its exact identity.");
        foreach (var lane in snapshot.Lanes)
        {
            var run = KnownRunConfigurations.Get(DirectSnapshotHistory.RunId(lane.Provenance.Id));
            if (lane.Provenance.Build != snapshot.Build ||
                lane.Provenance.ExpectedPartitions != 0 || lane.Partitions.Length != 0 ||
                lane.Provenance.HelixJobId != "unavailable" ||
                lane.Provenance.RuntimeVersion != "unavailable" ||
                lane.Provenance.V8Version != "unavailable" ||
                lane.Provenance.WorkloadVersion != "unavailable" ||
                lane.Provenance.HostSdkVersion is not null ||
                lane.Provenance.InstallerSdkSha is not null ||
                lane.Provenance.BenchmarkDotNetVersion is not null ||
                lane.Provenance.MeasurementConfiguration is not null ||
                lane.Provenance.Configuration != Uri.UnescapeDataString(run.IndexUri!.AbsolutePath))
                throw new InvalidDataException("Public snapshots cannot assert Helix partition provenance.");
            foreach (var value in lane.Measurements)
            {
                var stats = value.Statistics;
                var report = new Uri(run.IndexUri!, Uri.EscapeDataString(value.Identity.DisplayName) + ".html");
                if (string.IsNullOrWhiteSpace(value.Identity.ExactName) ||
                    value.Report != report.AbsoluteUri || value.Partition != "unavailable" ||
                    value.Categories.Length != 0 ||
                    stats.N is not null || stats.StandardError is not null ||
                    stats.StandardDeviation is not null || stats.Variance is not null ||
                    stats.Median is not null || stats.Min is not null || stats.Max is not null ||
                    stats.OriginalValues is not null || value.MeasurementCount is not null ||
                    value.MeasurementConfiguration is not null)
                    throw new InvalidDataException("Public snapshots must keep unavailable statistics and coverage explicit.");
                if (value.PublishedError is { } error && (!double.IsFinite(error) || error < 0) &&
                    value.InvalidReason is null)
                    throw new InvalidDataException("Invalid published error requires an explicit invalid reason.");
            }
        }
    }
}
