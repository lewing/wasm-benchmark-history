using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using WasmBenchmarkHistory.Components;
using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class HistoryChartTests
{
    private static readonly DateTime Start = new(2026, 11, 1);
    private const string Benchmark = "Example.Run";
    private const string RuntimeSha = "1111111111111111111111111111111111111111";
    private const string PerformanceSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task GapBands_FollowPublishedLinesEvenWhenFallbacksFillTheGap()
    {
        var history = History("baseline", [0, 1, 2, 10, 11, 12]);
        history = history with
        {
            Observations = history.Observations.Concat(
                Enumerable.Range(3, 7).Select(day =>
                    Observation("baseline", day) with { Source = ObservationSource.DirectSnapshot }))
                .ToArray()
        };

        var html = await RenderAsync([history]);

        Assert.Contains("class=\"history-gap-band\"", html);
        Assert.Contains("No published observations for 8 days", html);
    }

    [Fact]
    public async Task NormalizedAnnotations_ExcludeUnmatchedRawObservations()
    {
        var histories = new[] { History("baseline", [0, 1, 2, 10]), History("candidate", [0, 1, 2, 10]) }
            .Select(history => history with
            {
                Observations = history.Observations.Select(observation =>
                    observation.Timestamp == Start.AddDays(10)
                        ? observation with { PerformanceSha = new string('b', 40) }
                        : observation).ToArray()
            })
            .ToArray();
        var matches = ObservationMatcher.MatchStrict(histories)
            .Where(match => match.Key.Timestamp < Start.AddDays(10))
            .ToArray();

        var html = await RenderAsync(histories, matches, normalized: true);

        Assert.DoesNotContain("class=\"performance-revision-marker\"", html);
        Assert.DoesNotContain("class=\"history-gap-band\"", html);
    }

    [Fact]
    public async Task RevisionMarkers_DescribeTheFirstObservedNewRevision()
    {
        var history = History("baseline", [0, 1, 2]);
        history = history with
        {
            Observations = history.Observations.Select(observation =>
                observation.Timestamp == Start.AddDays(2)
                    ? observation with { PerformanceSha = new string('b', 40) }
                    : observation).ToArray()
        };

        var html = await RenderAsync([history]);

        Assert.Contains("class=\"performance-revision-marker\"", html);
        Assert.Contains("2026-11-03 00:00:00", html);
        Assert.Contains("aaaaaaaaaa", html);
        Assert.Contains("bbbbbbbbbb", html);
    }

    private static BenchmarkHistory History(string runId, int[] days) =>
        new(
            Benchmark,
            new RunConfiguration(runId, runId, null, "#4f7cff", runId),
            null,
            days.Select(day => Observation(runId, day)).ToArray());

    private static BenchmarkObservation Observation(string runId, int day) =>
        new(Benchmark, runId, Start.AddDays(day), 100, null, RuntimeSha, PerformanceSha, null);

    private static async Task<string> RenderAsync(
        IReadOnlyList<BenchmarkHistory> histories,
        IReadOnlyList<MatchedComparison>? matches = null,
        bool normalized = false)
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IJSRuntime, NoJavaScriptRuntime>()
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(
            services,
            services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<HistoryChart>(
                ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(HistoryChart.Histories)] = histories,
                    [nameof(HistoryChart.Matches)] = matches ?? [],
                    [nameof(HistoryChart.Normalized)] = normalized
                }));
            return root.ToHtmlString();
        });
    }

    private sealed class NoJavaScriptRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new NotSupportedException("Static chart rendering must not invoke JavaScript.");

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) =>
            throw new NotSupportedException("Static chart rendering must not invoke JavaScript.");
    }
}
