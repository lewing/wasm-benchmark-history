using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System.Text.RegularExpressions;
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

        var html = await RenderAsync(histories, matches, normalized: true, showRevisions: true);

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

        var html = await RenderAsync([history], showRevisions: true);

        Assert.Contains("class=\"performance-revision-marker\"", html);
        Assert.Contains("2026-11-03 00:00:00", html);
        Assert.Contains("aaaaaaaaaa", html);
        Assert.Contains("bbbbbbbbbb", html);
    }

    [Fact]
    public async Task RevisionMarkers_AreHiddenByDefaultWithoutHidingGaps()
    {
        var history = History("baseline", [0, 1, 2, 10]);
        history = history with
        {
            Observations = history.Observations.Select(observation =>
                observation.Timestamp == Start.AddDays(10)
                    ? observation with { PerformanceSha = new string('b', 40) }
                    : observation).ToArray()
        };

        var html = await RenderAsync([history]);

        Assert.Contains("class=\"history-gap-band\"", html);
        Assert.DoesNotContain("class=\"performance-revision-marker\"", html);
        Assert.DoesNotContain("Performance revision at", html);
    }

    [Fact]
    public async Task SharedTimeline_AlignsSparseHistoriesWithoutStretchingTheirPoints()
    {
        var bounds = (Start, Start.AddDays(4));
        var early = await RenderAsync([History("baseline", [0, 2])], timelineBounds: bounds);
        var late = await RenderAsync([History("baseline", [2, 4])], timelineBounds: bounds);

        Assert.Equal(["86", "528"], PointPositions(early));
        Assert.Equal(["528", "970"], PointPositions(late));
    }

    [Fact]
    public async Task SharedViewport_DoesNotResetAChartWhoseDataFallsOutsideIt()
    {
        var html = await RenderAsync(
            [History("baseline", [0, 1])],
            timelineBounds: (Start, Start.AddDays(10)),
            timeRange: HistoryTimeRange.Custom(Start.AddDays(5), Start.AddDays(10)));

        Assert.Contains("No observations in this range.", html);
        Assert.Empty(PointPositions(html));
    }

    [Fact]
    public async Task StackedCharts_HaveDistinctAccessibleDescriptionsIncludingVariabilityPanels()
    {
        var history = History("baseline", [0, 1, 2, 10]);
        var html = await RenderAsync([history], showVariability: true)
            + await RenderAsync([history], showVariability: true);
        var ids = Regex.Matches(html, "\\bid=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value).ToArray();
        var descriptions = Regex.Matches(html, "aria-(?:describedby|labelledby)=\"([^\"]+)\"")
            .SelectMany(match => match.Groups[1].Value.Split(' '));

        Assert.NotEmpty(ids);
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(descriptions, id => Assert.Contains(id, ids));
    }

    [Fact]
    public async Task CompactRows_DrawDatesOnlyOnTheAxisRowAndKeepPanelsOut()
    {
        var history = History("baseline", [0, 1, 2]);
        var inner = await RenderAsync([history], showVariability: true, compact: true, showTimeAxis: false);
        var bottom = await RenderAsync([history], showVariability: true, compact: true, showTimeAxis: true);

        Assert.DoesNotContain("text-anchor=\"middle\"", inner);
        Assert.Contains("text-anchor=\"middle\"", bottom);
        Assert.Contains("class=\"sync-crosshair\"", inner);
        Assert.DoesNotContain("variability-analysis", inner);
        Assert.DoesNotContain("chart-toolbar", inner);
    }

    [Fact]
    public async Task FocusedRuns_DimOnlyTheOtherSeries()
    {
        var html = await RenderAsync(
            [History("baseline", [0, 1]), History("other", [0, 1])],
            focusedRunIds: ["other"]);

        Assert.Single(Regex.Matches(html, "series-group is-dimmed"));
        Assert.Single(Regex.Matches(html, "series-group is-focused"));
    }

    private static string[] PointPositions(string html) =>
        Regex.Matches(html, "class=\"series-point\"\\s+cx=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value).ToArray();

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
        bool normalized = false,
        bool showRevisions = false,
        (DateTime Start, DateTime End)? timelineBounds = null,
        HistoryTimeRange? timeRange = null,
        bool showVariability = false,
        IReadOnlyCollection<string>? focusedRunIds = null,
        bool compact = false,
        bool showTimeAxis = true)
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
                    [nameof(HistoryChart.Normalized)] = normalized,
                    [nameof(HistoryChart.ShowPerformanceRevisionMarkers)] = showRevisions,
                    [nameof(HistoryChart.TimelineBounds)] = timelineBounds,
                    [nameof(HistoryChart.TimeRange)] = timeRange ?? HistoryTimeRange.All,
                    [nameof(HistoryChart.ShowVariabilityBand)] = showVariability,
                    [nameof(HistoryChart.FocusedRunIds)] = focusedRunIds,
                    [nameof(HistoryChart.Compact)] = compact,
                    [nameof(HistoryChart.ShowTimeAxis)] = showTimeAxis
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
