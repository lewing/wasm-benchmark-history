using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using WasmBenchmarkHistory.Components;
using WasmBenchmarkHistory.Components.Pages;
using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class BenchmarkExplorerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedSelection_RendersIndependentResultsAndOneActiveDetailsPanel(bool failSecond)
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<NavigationManager>(new TestNavigation())
            .AddSingleton<IBenchmarkHistoryOrchestrator>(new TestHistories(failSecond))
            .AddSingleton<IJSRuntime, NoJavaScriptRuntime>()
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<Home>(ParameterView.Empty);
            return root.ToHtmlString();
        });

        Assert.Contains("aria-multiselectable=\"true\"", html);
        Assert.Contains("aria-pressed=\"true\"", html);
        Assert.Contains("aria-pressed=\"false\"", html);
        Assert.Equal(2, html.Split("class=\"benchmark-chart").Length - 1);
        Assert.Equal(1, html.Split("Investigation: Bench.A").Length - 1);
        Assert.Contains("Bench.A", html);
        if (failSecond)
        {
            Assert.Contains("History unavailable for Bench.B.", html);
            Assert.Equal(1, html.Split("Historical benchmark line chart").Length - 1);
        }
        else
        {
            Assert.Equal(2, html.Split("Historical benchmark line chart").Length - 1);
        }
    }

    [Fact]
    public async Task IntervalPanel_RendersEveryBenchmarkRunPairAndMissingHistories()
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(
            services,
            services.GetRequiredService<ILoggerFactory>());
        var mono = KnownRunConfigurations.Get("mono-wasm");
        var coreClr = KnownRunConfigurations.Get("coreclr-wasm");
        var start = new DateTime(2026, 1, 1);
        var histories = new[]
        {
            History("Bench.A", mono, start, [10, 20, 40]),
            History("Bench.B", coreClr, start, [100, 50, 25])
        };
        var baseline = new InvestigationPin(
            mono.Id,
            new ObservationIdentity(start, new string('a', 40), new string('b', 40)));
        var comparison = new InvestigationPin(
            coreClr.Id,
            new ObservationIdentity(start.AddDays(10), new string('c', 40), new string('d', 40)));

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<RegressionInvestigationPanel>(
                ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(RegressionInvestigationPanel.Histories)] = histories,
                    [nameof(RegressionInvestigationPanel.Benchmarks)] = new[] { "Bench.A", "Bench.B" },
                    [nameof(RegressionInvestigationPanel.RunIds)] = new[] { mono.Id, coreClr.Id },
                    [nameof(RegressionInvestigationPanel.BaselinePin)] = baseline,
                    [nameof(RegressionInvestigationPanel.ComparisonPin)] = comparison
                }));
            return root.ToHtmlString();
        });

        Assert.Equal(5, html.Split("<tr ").Length - 1);
        Assert.Equal(2, html.Split("No history available for this benchmark/runtime.").Length - 1);
        Assert.Contains("Bench.A", html);
        Assert.Contains("Bench.B", html);
        Assert.Contains("Mono Wasm", html);
        Assert.Contains("CoreCLR Wasm", html);
        Assert.Contains("0m from target", html);
    }

    private static BenchmarkHistory History(
        string benchmark,
        RunConfiguration run,
        DateTime start,
        double[] values) =>
        new(benchmark, run, null, values.Select((value, index) =>
            new BenchmarkObservation(
                benchmark,
                run.Id,
                start.AddDays(index * 5),
                value,
                null,
                new string((char)('a' + index), 40),
                new string((char)('f' - index), 40),
                $"build-{index}")).ToArray());

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize(
            "http://localhost/",
            "http://localhost/?benchmark=Bench.A&selected=Bench.A&selected=Bench.B&runs=mono-wasm,mono-wasm-aot");
    }

    private sealed class TestHistories(bool failSecond) : IBenchmarkHistoryOrchestrator
    {
        public Task<BenchmarkCatalog> LoadCatalogAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new BenchmarkCatalog(new[] { "Bench.A", "Bench.B" }.Select(name =>
                new BenchmarkCatalogEntry(name, new Dictionary<string, Uri>(),
                    new HashSet<string> { "mono-wasm", "mono-wasm-aot" }))));

        public Task<IReadOnlyList<BenchmarkHistory>> LoadHistoriesAsync(
            BenchmarkCatalog catalog,
            string benchmark,
            IReadOnlyList<string> runIds,
            CancellationToken cancellationToken = default)
        {
            if (failSecond && benchmark == "Bench.B")
            {
                throw new BenchmarkDataException(
                    BenchmarkDataError.UnknownBenchmark, "History unavailable for Bench.B.");
            }

            IReadOnlyList<BenchmarkHistory> histories = runIds.Select(id =>
                new BenchmarkHistory(benchmark, KnownRunConfigurations.Get(id), null,
                    [new BenchmarkObservation(benchmark, id, new DateTime(2026, 11, 1),
                        100, null, new string('a', 40), new string('b', 40), null)]))
                .ToArray();
            return Task.FromResult(histories);
        }
    }

    private sealed class NoJavaScriptRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new NotSupportedException("Static explorer rendering must not invoke JavaScript.");

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier, CancellationToken cancellationToken, object?[]? args) =>
            throw new NotSupportedException("Static explorer rendering must not invoke JavaScript.");
    }
}
