using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class ChangeSetHistoryAnalysisTests
{
    [Fact]
    public void ImportedTarget_MapsExactKnownRunAndPreservesIdentity()
    {
        var uri = new Uri(
            "https://pvscmdupload.z22.web.core.windows.net/reports/allTestHistory/refs/heads/main_x64_ubuntu%2022.04_CompilationMode=wasm_RunKind=micro/ViperUbuntu/Example.Generic(String).Run(Path%3a%20%22a%2Fb%22).html");

        var target = ImportedHistoryTarget.Parse(uri);

        Assert.Equal("mono-wasm", target.Run.Id);
        Assert.Equal(
            "Example.Generic(String).Run(Path: \"a/b\")",
            target.Benchmark);
    }

    [Fact]
    public void ImportedTarget_RejectsUnknownReportRun()
    {
        var exception = Assert.Throws<BenchmarkDataException>(
            () => ImportedHistoryTarget.Parse(
                new Uri("https://pvscmdupload.z22.web.core.windows.net/reports/allTestHistory/unknown/Test.html")));

        Assert.Contains("known report run", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyzer_ProducesStableContextAndExactDeepLink()
    {
        var history = History(
            Enumerable.Range(0, 14)
                .Select(index => Observation(
                    index,
                    index < 7 ? 100 + index % 2 : 80 + index % 2,
                    index == 6 ? "1111111" : index == 13 ? "2222222" : $"{index + 100:x7}",
                    "aaaaaaa"))
                .ToArray());

        var result = ChangeSetHistoryAnalyzer.Analyze(
            history,
            "1111111",
            "2222222",
            "aaaaaaa");

        Assert.Equal(HistoryBoundaryAssessment.Stable, result.Assessment);
        Assert.Equal(7, result.Baseline.Trailing.Count);
        Assert.Equal(7, result.Compare.Trailing.Count);
        var state = HistoryPageStateCodec.Parse(result.BenchmarkViewUri);
        Assert.Empty(state.Warnings);
        Assert.Equal(history.Benchmark, state.State.Benchmark);
        Assert.Equal("mono-wasm", state.State.InvestigationRunId);
        Assert.Equal("1111111", state.State.BaselineA?.RuntimeSha);
        Assert.Equal("2222222", state.State.ComparisonB?.RuntimeSha);
    }

    [Fact]
    public void Analyzer_ReportsMissingSha()
    {
        var exception = Assert.Throws<BenchmarkDataException>(
            () => ChangeSetHistoryAnalyzer.Analyze(
                History(Observation(0, 10, "1111111", "aaaaaaa")),
                "1111111",
                "2222222"));

        Assert.Contains("compare runtime SHA was not found", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyzer_ReportsAmbiguousShaWithoutPerformanceIdentity()
    {
        var exception = Assert.Throws<BenchmarkDataException>(
            () => ChangeSetHistoryAnalyzer.Analyze(
                History(
                    Observation(0, 10, "1111111", "aaaaaaa"),
                    Observation(1, 11, "1111111", "bbbbbbb"),
                    Observation(2, 9, "2222222", "aaaaaaa")),
                "1111111",
                "2222222"));

        Assert.Contains("ambiguous", exception.Message, StringComparison.Ordinal);
        Assert.Contains("performance SHA", exception.Message, StringComparison.Ordinal);
    }

    private static BenchmarkHistory History(params BenchmarkObservation[] observations) =>
        new(
            "Example.Generic(String).Run(Path: \"a/b\")",
            KnownRunConfigurations.Get("mono-wasm"),
            "primary",
            observations);

    private static BenchmarkObservation Observation(
        int index,
        double value,
        string runtimeSha,
        string performanceSha) =>
        new(
            "Example.Generic(String).Run(Path: \"a/b\")",
            "mono-wasm",
            new DateTime(2026, 8, 1).AddDays(index),
            value,
            .1,
            runtimeSha,
            performanceSha,
            "primary");
}
