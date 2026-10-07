using System.Net;
using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class PublishedBuildSnapshotTests
{
    private static readonly ObservationKey Key = new(
        new DateTime(2026, 10, 6, 9, 38, 57), new string('a', 40), new string('b', 40));

    [Fact]
    public void Create_UsesExactIdentityAndKeepsUnavailableStatisticsExplicit()
    {
        var snapshot = Create();
        var result = BuildComparison.Analyze(snapshot);

        Assert.True(snapshot.IsPublishedHistory);
        Assert.Equal(Key, snapshot.PublishedIdentity);
        Assert.Equal("unavailable", snapshot.Build.BuildNumber);
        Assert.Equal(PublishedBuildSnapshot.CaptureSource, snapshot.CaptureSource);
        Assert.Equal(5, snapshot.Lanes.Length);
        Assert.Single(result.Common);
        Assert.Equal(10, BuildComparison.Summarize(result.Rows, result.LaneIds).Length);
        Assert.All(snapshot.Lanes, lane =>
        {
            Assert.Empty(lane.Partitions);
            Assert.Equal(0, lane.Provenance.ExpectedPartitions);
            var value = Assert.Single(lane.Measurements);
            Assert.Equal(10, value.Statistics.Mean);
            Assert.Equal(.5, value.PublishedError);
            Assert.Null(value.Statistics.N);
            Assert.Null(value.Statistics.StandardError);
            Assert.Null(value.Statistics.Variance);
            Assert.StartsWith("https://pvscmdupload.z22.web.core.windows.net/reports/", value.Report);
        });
        Assert.Contains("Published", snapshot.DisplayLabel);
    }

    [Fact]
    public void Create_DoesNotJoinSameCommitAtDifferentTimestampOrDifferentPerformanceCommit()
    {
        var snapshot = Create([
            Observation(10),
            Observation(99) with { Timestamp = Key.Timestamp.AddHours(1) },
            Observation(999) with { PerformanceSha = new string('c', 40) }
        ]);

        Assert.All(snapshot.Lanes, lane => Assert.Equal(10, Assert.Single(lane.Measurements).Statistics.Mean));
    }

    [Fact]
    public void Create_DuplicatesAndInvalidMeansRemainExcluded()
    {
        var duplicate = BuildComparison.Analyze(Create([Observation(10), Observation(10)]));
        Assert.Empty(duplicate.Common);
        Assert.All(duplicate.Rows.Single().Cells.Values, cell => Assert.Equal("duplicate", cell.Status));

        var invalid = Create([Observation(double.NaN)]);
        var result = BuildComparison.Analyze(invalid);
        Assert.Empty(result.Common);
        Assert.All(result.Rows.Single().Cells.Values, cell => Assert.Equal("invalid", cell.Status));
        Assert.All(invalid.Lanes, lane =>
        {
            Assert.Null(lane.Measurements[0].Statistics.Mean);
            Assert.NotNull(lane.Measurements[0].InvalidReason);
        });
    }

    [Fact]
    public void PublishedSnapshotUsesSamePairwiseRankingWithoutRequiringInventedSampleCount()
    {
        var snapshot = Create();
        var lanes = snapshot.Lanes.Select(lane => lane.Provenance.Id == BuildComparison.CoreClrR2RComposite
            ? lane with
            {
                Measurements = [lane.Measurements[0] with
                {
                    Statistics = lane.Measurements[0].Statistics with { Mean = 15 }
                }]
            } : lane).ToArray();

        var ranking = BenchmarkRanking.Create(BuildComparison.Analyze(snapshot with { Lanes = lanes }),
            BuildComparison.CoreClrR2RComposite, "coreclr-r2r", 100);

        Assert.Equal(50, Assert.Single(ranking.Rows).SlowdownPercent);
        Assert.Equal(1, ranking.ComparableCount);
    }

    [Fact]
    public void DirectStatisticsValidationAndArchiveRemainStrict()
    {
        var snapshot = Create();
        Assert.Equal("Statistics.N must be positive.", BuildComparison.InvalidReason(snapshot.Lanes[0].Measurements[0].Statistics));
        Assert.Throws<InvalidDataException>(() => DirectHistoryArchiveBuilder.Create(
            [snapshot], 7, DateTimeOffset.UtcNow));
        Assert.Empty(DirectSnapshotHistory.GetAvailability(new[] { snapshot }));
        Assert.Null(DirectSnapshotHistory.CreateHistory(
            "N.T.Run", KnownRunConfigurations.All[0], new[] { snapshot }));
        Assert.Throws<InvalidDataException>(() => BuildComparison.Validate(snapshot with
        {
            PublishedIdentity = Key with { Timestamp = Key.Timestamp.AddSeconds(1) }
        }));
        Assert.Throws<InvalidDataException>(() => BuildComparison.Validate(snapshot with
        {
            Lanes = snapshot.Lanes.Select(lane => lane with
            {
                Measurements = lane.Measurements.Select(value => value with
                {
                    Statistics = value.Statistics with { N = 1 }
                }).ToArray()
            }).ToArray()
        }));
    }

    [Fact]
    public void OrderingIncludesPublicIdsButNotOtherArchivesAndUsesMeasurementDates()
    {
        var id = PublishedBuildSnapshot.Id(Key);
        Assert.Equal([id, "3074629", "3068640"],
            BuildSnapshotStoreOrdering.OrderBuildIds(["3068640", "direct-history", id, "3074629", "local"]));
        var descriptors = BuildSnapshotStoreOrdering.OrderDescriptors(
        [
            new(id, "unavailable", PublishedBuildSnapshot.CaptureSource, null, "2026-10-06 09:38:57"),
            new("99", "older", "Direct Helix snapshot", DateTimeOffset.UtcNow, "2026-09-01T00:00:00Z"),
            new("100", "newer", "Direct Helix snapshot", null, "2026-10-07T00:00:00Z")
        ]);
        Assert.Equal(["100", id, "99"], descriptors.Select(value => value.BuildId));
    }

    [Fact]
    public void ParserPreservesInvalidNumericPointsOnlyWhenExplicitlyRequested()
    {
        var html = HistoryHtml();
        var parser = new BenchmarkHistoryParser();
        Assert.Throws<BenchmarkDataException>(() => parser.Parse("N.T.Run", KnownRunConfigurations.All[0], html));

        var history = parser.Parse("N.T.Run", KnownRunConfigurations.All[0], html, preserveInvalidMeasurements: true);

        Assert.True(double.IsNaN(history.Observations[0].Value));
        Assert.Equal(10, history.Observations[1].Value);
    }

    [Fact]
    public async Task AcquisitionWritesLoadableSnapshotAndReusesOnlyExactMatches()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wasm-benchmark-history-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var handler = new PublicHandler();
            using var http = new HttpClient(handler);
            var progress = new List<string>();
            var acquirer = new PublishedSnapshotAcquirer(http, progress.Add);
            var path = Path.Combine(directory, PublishedBuildSnapshot.Id(Key) + ".json.gz");
            await acquirer.AcquireAsync(Key, path, Path.Combine(directory, "cache"));
            var snapshot = await BuildSnapshotImporter.ReadAsync(path);
            Assert.Single(BuildComparison.Analyze(snapshot).Common);
            Assert.Equal(5, handler.HistoriesFetched);

            await acquirer.AcquireAsync(Key, path, Path.Combine(directory, "cache"));
            Assert.Equal(5, handler.HistoriesFetched);
            Assert.Contains(progress, line => line.StartsWith("Wrote ", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AcquisitionWithNoCompositeMatchWritesNothingAndRetriesMissingObservations()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wasm-benchmark-history-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var handler = new PublicHandler { OmitCompositeMatch = true };
            using var http = new HttpClient(handler);
            var acquirer = new PublishedSnapshotAcquirer(http, _ => { });
            var path = Path.Combine(directory, PublishedBuildSnapshot.Id(Key) + ".json.gz");
            var cache = Path.Combine(directory, "cache");

            await Assert.ThrowsAsync<InvalidDataException>(() => acquirer.AcquireAsync(Key, path, cache));
            Assert.False(File.Exists(path));

            handler.OmitCompositeMatch = false;
            var snapshot = await acquirer.AcquireAsync(Key, path, cache);
            Assert.Single(BuildComparison.Analyze(snapshot).Common);
            Assert.Equal(6, handler.HistoriesFetched);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static BuildSnapshot Create(BenchmarkObservation[]? points = null) =>
        PublishedBuildSnapshot.Create(Key, KnownRunConfigurations.All.ToDictionary(
            run => run.Id,
            IReadOnlyList<BenchmarkHistory> (run) =>
            [
                new("N.T.Run", run, null, (points ?? [Observation(10)])
                    .Select(value => value with { RunId = run.Id }).ToArray())
            ]), DateTimeOffset.UtcNow);

    private static BenchmarkObservation Observation(double value) =>
        new("N.T.Run", "", Key.Timestamp, value, .5, Key.RuntimeSha, Key.PerformanceSha, null);

    private static string HistoryHtml() => $$"""
        var defaultCounter = new CounterEntry(7, "fixture");
        trendData[7] = [{
            "x": ['2026-10-05 09:38:57', '2026-10-06 09:38:57'],
            "y": [NaN, 10],
            "gitHash": { "runtime": ['{{Key.RuntimeSha}}','{{Key.RuntimeSha}}'] },
            "perfRepoHash": ['{{Key.PerformanceSha}}','{{Key.PerformanceSha}}'],
            "error_y": { "array": [null, .5] }
        }];
        """;

    private sealed class PublicHandler : HttpMessageHandler
    {
        public int HistoriesFetched;
        public bool OmitCompositeMatch;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var index = uri.AbsolutePath.EndsWith("AllTestindex.html", StringComparison.Ordinal);
            if (!index)
                Interlocked.Increment(ref HistoriesFetched);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(index
                    ? $"<a href=\"{new Uri(uri, "N.T.Run.html")}\">N.T.Run</a>"
                    : OmitCompositeMatch && uri.AbsolutePath.Contains("r2r_composite", StringComparison.Ordinal)
                        ? HistoryHtml().Replace("2026-10-06 09:38:57", "2026-10-07 09:38:57", StringComparison.Ordinal)
                        : HistoryHtml())
            });
        }
    }
}
