using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class BenchmarkHistoryParserTests
{
    [Fact]
    public void Parse_UsesOnlyPrimaryTraceAndAlignsMetadata()
    {
        var parser = new BenchmarkHistoryParser();
        var run = KnownRunConfigurations.All[0];

        var history = parser.Parse("Fixture.Benchmark", run, Fixture.Read("history.html"));

        Assert.Equal("primary 'trace", history.TraceName);
        Assert.Collection(
            history.Observations,
            observation =>
            {
                Assert.Equal(new DateTime(2026, 8, 30, 10, 20, 30), observation.Timestamp);
                Assert.Equal(10.5, observation.Value);
                Assert.Equal(0.25, observation.Error);
                Assert.Equal("runtime-a", observation.RuntimeSha);
                Assert.Equal("performance-a", observation.PerformanceSha);
            },
            observation =>
            {
                Assert.Equal(11.75, observation.Value);
                Assert.Null(observation.Error);
                Assert.Equal("runtime-b", observation.RuntimeSha);
                Assert.Equal("performance-b", observation.PerformanceSha);
            });
        Assert.DoesNotContain(history.Observations, observation => observation.Value == 999);
    }

    [Fact]
    public void Parse_AcceptsTraceWithoutErrorArray()
    {
        const string html = """
            var defaultCounter = new CounterEntry(7, "fixture");
            trendData[7] = [{
                "x": ['2026-09-01 01:02:03'],
                "y": [4.5],
                "gitHash": { "runtime": ['runtime'] },
                "perfRepoHash": ['performance']
            }];
            """;

        var history = new BenchmarkHistoryParser().Parse(
            "NoError.Benchmark",
            KnownRunConfigurations.All[0],
            html);

        Assert.Single(history.Observations);
        Assert.Null(history.Observations[0].Error);
        Assert.Null(history.TraceName);
    }

    [Fact]
    public void Parse_RejectsMisalignedPrimaryFields()
    {
        const string html = """
            var defaultCounter = new CounterEntry(7, "fixture");
            trendData[7] = [{
                "x": ['2026-09-01 01:02:03'],
                "y": [4.5, 5.5],
                "gitHash": { "runtime": ['runtime'] },
                "perfRepoHash": ['performance']
            }];
            """;

        var exception = Assert.Throws<BenchmarkDataException>(() =>
            new BenchmarkHistoryParser().Parse(
                "Broken.Benchmark",
                KnownRunConfigurations.All[0],
                html));

        Assert.Equal(BenchmarkDataError.Schema, exception.Error);
        Assert.Contains("not aligned", exception.Message);
    }
}
