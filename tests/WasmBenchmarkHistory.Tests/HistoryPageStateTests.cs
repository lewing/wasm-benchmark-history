using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class HistoryPageStateTests
{
    [Fact]
    public void StateRoundTripsAllInvestigationFields()
    {
        var from = new DateTime(2025, 1, 2, 3, 4, 5);
        var to = from.AddDays(30);
        var state = new HistoryPageState(
            "Bench(value: \"a/b+c\")",
            ["mono-wasm", "coreclr-wasm"],
            true,
            HistoryTimeRange.Custom(from, to),
            "mono-wasm",
            new ObservationIdentity(from.AddDays(2), "abcdef1", "1234567"),
            new ObservationIdentity(from.AddDays(20), "abcdef2", "1234568"),
            true);

        var uri = HistoryPageStateCodec.ToRelativeUri(state);
        var result = HistoryPageStateCodec.Parse(uri);

        Assert.Empty(result.Warnings);
        Assert.Equal(state.Benchmark, result.State.Benchmark);
        Assert.Equal(state.RunIds, result.State.RunIds);
        Assert.Equal(state.Normalized, result.State.Normalized);
        Assert.Equal(state.TimeRange, result.State.TimeRange);
        Assert.Equal(state.InvestigationRunId, result.State.InvestigationRunId);
        Assert.Equal(state.BaselineA, result.State.BaselineA);
        Assert.Equal(state.ComparisonB, result.State.ComparisonB);
        Assert.Equal(state.UseRobustSummary, result.State.UseRobustSummary);
    }

    [Fact]
    public void InvalidSharedValuesAreIgnoredWithWarnings()
    {
        var result = HistoryPageStateCodec.Parse(
            "/?benchmark=&runs=unknown&mode=bad&range=custom&from=nope&to=4&investigate=bad&a=also.bad");

        Assert.Null(result.State.Benchmark);
        Assert.Empty(result.State.RunIds);
        Assert.False(result.State.Normalized);
        Assert.Equal(HistoryRangeKind.All, result.State.TimeRange.Kind);
        Assert.Null(result.State.InvestigationRunId);
        Assert.Null(result.State.BaselineA);
        Assert.True(result.Warnings.Count >= 5);
    }

    [Fact]
    public void RepeatedSensitiveValuesAreRejected()
    {
        var result = HistoryPageStateCodec.Parse(
            "/?benchmark=one&benchmark=two&runs=mono-wasm,coreclr-wasm");

        Assert.Null(result.State.Benchmark);
        Assert.Contains(
            result.Warnings,
            warning => warning.Contains("repeated", StringComparison.Ordinal));
    }

    [Fact]
    public void MalformedEscapesDoNotThrow()
    {
        var result = HistoryPageStateCodec.Parse("/?benchmark=%ZZ");

        Assert.Null(result.State.Benchmark);
        Assert.Contains(
            result.Warnings,
            warning => warning.Contains("malformed", StringComparison.Ordinal));
    }
}
