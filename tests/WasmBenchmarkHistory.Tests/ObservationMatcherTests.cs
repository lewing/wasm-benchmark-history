using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class ObservationMatcherTests
{
    [Fact]
    public void MatchStrict_RequiresTimestampAndBothShas()
    {
        var timestamp = new DateTime(2026, 9, 1, 10, 0, 0);
        var mono = History(
            KnownRunConfigurations.All[0],
            Observation(KnownRunConfigurations.All[0], timestamp, 10, "runtime-a", "performance-a"),
            Observation(KnownRunConfigurations.All[0], timestamp.AddHours(1), 11, "runtime-b", "performance-b"));
        var aot = History(
            KnownRunConfigurations.All[1],
            Observation(KnownRunConfigurations.All[1], timestamp, 5, "different-runtime", "performance-a"),
            Observation(KnownRunConfigurations.All[1], timestamp.AddHours(1), 5.5, "runtime-b", "performance-b"));

        var matches = ObservationMatcher.MatchStrict([mono, aot]);

        var match = Assert.Single(matches);
        Assert.Equal(timestamp.AddHours(1), match.Key.Timestamp);
        Assert.Equal(11, match.Observations[mono.Run.Id].Value);
        Assert.Equal(5.5, match.Observations[aot.Run.Id].Value);
    }

    [Fact]
    public void MatchStrict_RequiresKeyInEverySelectedRun()
    {
        var timestamp = new DateTime(2026, 9, 1, 10, 0, 0);
        var histories = KnownRunConfigurations.All
            .Select((run, index) => History(
                run,
                Observation(
                    run,
                    timestamp,
                    10 + index,
                    "runtime",
                    index == 2 ? "different-performance" : "performance")))
            .ToArray();

        Assert.Empty(ObservationMatcher.MatchStrict(histories));
    }

    private static BenchmarkObservation Observation(
        RunConfiguration run,
        DateTime timestamp,
        double value,
        string runtimeSha,
        string performanceSha) =>
        new(
            "Fixture.Benchmark",
            run.Id,
            timestamp,
            value,
            null,
            runtimeSha,
            performanceSha,
            null);

    private static BenchmarkHistory History(
        RunConfiguration run,
        params BenchmarkObservation[] observations) =>
        new("Fixture.Benchmark", run, null, observations);
}
