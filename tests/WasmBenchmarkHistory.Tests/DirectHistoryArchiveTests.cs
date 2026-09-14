using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class DirectHistoryArchiveTests
{
    [Fact]
    public void Create_OrdersDeduplicatesRetainsAndExcludesIncompleteBuilds()
    {
        var duplicateOlder = Snapshot("100", "2026-09-01T00:00:00Z", 10);
        var duplicateNewer = duplicateOlder with
        {
            CapturedAt = DateTimeOffset.Parse("2026-09-03T00:00:00Z")
        };
        var incomplete = Snapshot("102", "2026-09-03T00:00:00Z", 12) with
        {
            Lanes =
            [
                Snapshot("102", "2026-09-03T00:00:00Z", 12).Lanes[0] with
                    { Partitions = [], Measurements = [] },
                .. Snapshot("102", "2026-09-03T00:00:00Z", 12).Lanes.Skip(1)
            ]
        };

        var archive = DirectHistoryArchiveBuilder.Create(
            [Snapshot("101", "2026-09-02T00:00:00Z", 11), duplicateNewer, duplicateOlder, incomplete],
            1,
            DateTimeOffset.Parse("2026-09-04T00:00:00Z"));

        Assert.Equal("101", Assert.Single(archive.Builds).Build.BuildId);
        Assert.Contains(archive.Exclusions,
            value => value.BuildId == "100" && value.Reason.Contains("retention"));
        Assert.Contains(archive.Exclusions,
            value => value.BuildId == "102" && value.Reason.Contains("partitions"));
    }

    [Fact]
    public async Task CompactExport_IsDeterministicAndOmitsRawSamples()
    {
        var archive = DirectHistoryArchiveBuilder.Create(
            [Snapshot("100", "2026-09-01T00:00:00Z", 10)],
            7,
            DateTimeOffset.Parse("2026-09-04T00:00:00Z"));
        var first = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json.gz");
        var second = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json.gz");
        try
        {
            await DirectHistoryArchiveBuilder.WriteAsync(archive, first);
            await DirectHistoryArchiveBuilder.WriteAsync(archive, second);
            Assert.Equal(await File.ReadAllBytesAsync(first), await File.ReadAllBytesAsync(second));
            var loaded = await DirectHistoryArchiveBuilder.ReadAsync(first);
            var measurement = loaded.Builds[0].Lanes[0].Measurements[0];
            Assert.Equal(1, measurement.Variance);
            Assert.Equal(3, measurement.N);
            Assert.DoesNotContain("originalValues", await ReadGzipAsync(first),
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    [Fact]
    public void Trend_OrdersBuildsAndDisplaysVarianceAndRatios()
    {
        var archive = DirectHistoryArchiveBuilder.Create(
            [Snapshot("101", "2026-09-02T00:00:00Z", 20),
             Snapshot("100", "2026-09-01T00:00:00Z", 10)],
            7,
            DateTimeOffset.Parse("2026-09-04T00:00:00Z"));

        var rows = DirectHistoryTrend.Create(archive, "N.T.Run");

        Assert.Equal(["100", "101"], rows.Select(row => row.Build.BuildId));
        Assert.All(rows, row => Assert.Equal(1, row.Cells["coreclr-r2r"].Variance));
        Assert.All(rows, row => Assert.Equal(1, row.Cells["mono-interpreter"].SpeedupVsMono));
        Assert.Null(rows[0].Cells["coreclr-r2r"].SpeedupVsPrevious);
        Assert.Equal(.5, rows[1].Cells["coreclr-r2r"].SpeedupVsPrevious);
    }

    private static async Task<string> ReadGzipAsync(string path)
    {
        await using var file = File.OpenRead(path);
        await using var gzip = new System.IO.Compression.GZipStream(
            file, System.IO.Compression.CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return await reader.ReadToEndAsync();
    }

    private static BuildSnapshot Snapshot(string id, string timestamp, double mean)
    {
        var build = new BuildProvenance(
            id, id, new string('a', 40), new string('b', 40), timestamp);
        var lanes = BuildComparison.LaneIds.Select(lane =>
        {
            var measurement = new BuildMeasurement(
                new("N", "T", "Run", ""), ["Test"],
                new(mean, mean, 1, 1 / Math.Sqrt(3), 3, mean - 1, mean + 1, 1,
                    [mean - 1, mean, mean + 1]),
                "Partition0", "report.json", null, 3);
            return new BuildLane(
                new(lane, lane, Guid.Empty.ToString(), build, "12", "unavailable", "unavailable",
                    "RunKind=micro", 1),
                [new("Partition0", "passed", "", 1, 1, 0)],
                [measurement]);
        }).ToArray();
        return new(1, build, lanes, [], "Direct Helix snapshot",
            DateTimeOffset.Parse(timestamp).AddHours(1));
    }
}
