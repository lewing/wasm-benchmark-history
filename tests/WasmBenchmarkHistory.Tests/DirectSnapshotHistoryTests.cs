using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class DirectSnapshotHistoryTests
{
    [Fact]
    public void AvailabilityIncludesOnlyValidUniqueMeasurements()
    {
        var valid = Snapshot("3074629", Measurement("Valid", 10));
        var invalidMeasurement = Measurement("Invalid", 0) with
        {
            InvalidReason = "Mean must be finite and positive."
        };
        var invalid = Snapshot("3074630", invalidMeasurement);

        var availability = DirectSnapshotHistory.GetAvailability([valid, invalid]);

        Assert.All(BuildComparison.LaneIds, lane =>
            Assert.Contains(DirectSnapshotHistory.RunId(lane), availability["N.T.Valid"]));
        Assert.DoesNotContain("N.T.Invalid", availability.Keys);
    }

    [Fact]
    public void DirectOnlyR2RHistoryRetainsSamplesAndProvenance()
    {
        var snapshot = Snapshot("3074629", Measurement("Run", 10));
        var run = KnownRunConfigurations.Get("coreclr-wasm-r2r");

        var history = DirectSnapshotHistory.CreateHistory("N.T.Run", run, [snapshot]);

        var observation = Assert.Single(history!.Observations);
        Assert.Equal(ObservationSource.DirectSnapshot, observation.Source);
        Assert.Equal("3074629", observation.BuildId);
        Assert.Equal([9d, 10d, 11d], observation.Samples);
        Assert.Equal(snapshot.Build.RuntimeSha, observation.RuntimeSha);
        Assert.Equal(snapshot.Build.PerformanceSha, observation.PerformanceSha);
    }

    [Fact]
    public void MergePrefersEquivalentPublishedObservationAndRejectsConflict()
    {
        var timestamp = new DateTime(2026, 9, 13, 14, 59, 42);
        var published = Observation(timestamp, 10, ObservationSource.PublishedHistory);
        var direct = Observation(timestamp, 10, ObservationSource.DirectSnapshot);

        var merged = DirectSnapshotHistory.MergeObservations(
            "N.T.Run", "mono-wasm", [published], [direct]);

        Assert.Same(published, Assert.Single(merged.Observations));
        Assert.Empty(merged.Conflicts);
        var rounded = DirectSnapshotHistory.MergeObservations(
            "N.T.Run", "mono-wasm", [published], [direct with { Value = 10.004, Error = 999 }]);
        Assert.Same(published, Assert.Single(rounded.Observations));
        Assert.Empty(rounded.Conflicts);
        var conflict = DirectSnapshotHistory.MergeObservations(
            "N.T.Run", "mono-wasm", [published], [direct with { Value = 11 }]);
        Assert.Same(published, Assert.Single(conflict.Observations));
        Assert.Single(conflict.Conflicts);
    }

    [Fact]
    public void FourDirectHistoriesProduceOneStrictSameBuildMatch()
    {
        var snapshot = Snapshot("3074629", Measurement("Run", 10));
        var histories = KnownRunConfigurations.All.Select(run =>
            DirectSnapshotHistory.CreateHistory("N.T.Run", run, [snapshot])!).ToArray();

        var match = Assert.Single(ObservationMatcher.MatchStrict(histories));

        Assert.Equal(4, match.Observations.Count);
        Assert.All(match.Observations.Values,
            observation => Assert.Equal(ObservationSource.DirectSnapshot, observation.Source));
    }

    [Fact]
    public void OneDirectObservationDoesNotProduceVariabilityStatistics()
    {
        var history = DirectSnapshotHistory.CreateHistory(
            "N.T.Run", KnownRunConfigurations.Get("coreclr-wasm-r2r"),
            [Snapshot("3074629", Measurement("Run", 10))])!;

        Assert.Empty(RollingVariabilityCalculator.Calculate(
            history.Observations.Select(value => new VariabilitySample(value.Timestamp, value.Value)),
            RollingVariabilityCalculator.DefaultWindow));
    }

    private static BenchmarkObservation Observation(
        DateTime timestamp, double value, ObservationSource source) =>
        new("N.T.Run", "mono-wasm", timestamp, value, 1,
            new string('a', 40), new string('b', 40), "trace", source,
            source == ObservationSource.DirectSnapshot ? "3074629" : null);

    private static BuildMeasurement Measurement(string method, double mean) =>
        new(new("N", "T", method, ""), ["Test"],
            new(mean, mean, 1, 1 / Math.Sqrt(3), 3, mean - 1, mean + 1, 1,
                [mean - 1, mean, mean + 1]),
            "Partition0", "report.json", mean > 0 ? null : "invalid");

    private static BuildSnapshot Snapshot(string id, BuildMeasurement measurement)
    {
        var build = new BuildProvenance(
            id, "20260913.1", new string('a', 40), new string('b', 40),
            "2026-09-13T14:59:42+00:00");
        var lanes = BuildComparison.LaneIds.Select(lane => new BuildLane(
            new(lane, lane, Guid.Empty.ToString(), build, "12", "unavailable", "unavailable",
                "RunKind=micro", 15),
            [new("Partition0", "passed", "", 1, 1, 0)],
            [measurement])).ToArray();
        return new(1, build, lanes, [], "Direct Helix snapshot",
            DateTimeOffset.Parse("2026-09-13T23:30:00Z"));
    }
}
