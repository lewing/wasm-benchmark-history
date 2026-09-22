using System.Text.Json;
using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class DirectRunAcquirerTests
{
    [Theory]
    [InlineData("3074629")]
    [InlineData("https://dev.azure.com/dnceng/internal/_build/results?buildId=3074629&view=results")]
    public void NormalizeBuildUrl_AcceptsKnownInternalBuilds(string value)
    {
        Assert.Equal("https://dev.azure.com/dnceng/internal/_build/results?buildId=3074629",
            DirectRunAcquirer.NormalizeBuildUrl(value));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("https://example.com/dnceng/internal/_build/results?buildId=3074629")]
    [InlineData("https://dev.azure.com/dnceng/public/_build/results?buildId=3074629")]
    [InlineData("https://dev.azure.com/dnceng/internal/_build/results")]
    public void NormalizeBuildUrl_RejectsUnknownOrMalformedInputs(string value)
    {
        Assert.Throws<ArgumentException>(() => DirectRunAcquirer.NormalizeBuildUrl(value));
    }

    [Fact]
    public void CacheDirectory_RejectsCurrentWorktree()
    {
        Assert.Throws<ArgumentException>(() =>
            DirectRunAcquirer.ValidateCacheDirectory(Path.Combine(Environment.CurrentDirectory, "raw-cache")));
        DirectRunAcquirer.ValidateCacheDirectory(
            Path.Combine(Path.GetTempPath(), "wasm-direct-cache-" + Guid.NewGuid()));
    }

    [Fact]
    public void ParseTimeline_MapsExactWasmLanesAndLeavesMissingLaneExplicit()
    {
        using var timeline = JsonDocument.Parse("""
            {"records":[
              {"id":"mono","type":"Job","name":"Performance micro wasm wasm v8 linux  x64 perfviper net11.0"},
              {"id":"aot","type":"Job","name":"Performance micro wasm aot v8 linux  x64 perfviper net11.0"},
              {"id":"interp","type":"Job","name":"Performance micro wasm_coreclr wasm coreclr_v8 linux  x64 perfviper net11.0"},
              {"id":"r2r","type":"Job","name":"Performance micro wasm_coreclr wasm coreclr_r2r_v8 linux  x64 perfviper net11.0"},
              {"id":"send1","parentId":"mono","type":"Task","name":"Send job to Helix (Unix)","log":{"id":11}},
              {"id":"send2","parentId":"aot","type":"Task","name":"Send job to Helix (Unix)","log":{"id":12}},
              {"id":"send3","parentId":"interp","type":"Task","name":"Send job to Helix (Unix)","log":{"id":13}},
              {"id":"other","type":"Job","name":"Performance micro coreclr JIT linux"}
            ]}
            """);

        var lanes = DirectRunAcquirer.ParseTimeline(timeline.RootElement);

        Assert.Equal(BuildComparison.LaneIds, lanes.Select(lane => lane.Id));
        Assert.Equal(new int?[] { 11, 12, 13, null }, lanes.Select(lane => lane.LogId));
    }

    [Fact]
    public void ParseCandidateBuildIds_FiltersAndOrdersExpectedPipelineBuilds()
    {
        using var builds = JsonDocument.Parse("""
            {"results":[
              {"id":3,"status":"completed","sourceBranch":"refs/heads/main","definition":{"id":702}},
              {"id":5,"status":"inProgress","sourceBranch":"refs/heads/main","definition":{"id":702}},
              {"id":4,"status":"completed","sourceBranch":"refs/heads/release","definition":{"id":702}},
              {"id":2,"status":"completed","sourceBranch":"refs/heads/main","definition":{"id":702}},
              {"id":6,"status":"completed","sourceBranch":"refs/heads/main","definition":{"id":999}}
            ]}
            """);

        Assert.Equal(["3", "2"], DirectRunAcquirer.ParseCandidateBuildIds(builds.RootElement, 10));
    }

    [Fact]
    public void ParseTimeline_RejectsDuplicateExpectedLane()
    {
        using var timeline = JsonDocument.Parse("""
            {"records":[
              {"id":"one","type":"Job","name":"Performance micro wasm wasm v8 linux  x64 perfviper net11.0"},
              {"id":"two","type":"Job","name":"Performance micro wasm wasm v8 linux  x64 perfviper net11.0 retry"}
            ]}
            """);

        Assert.Throws<InvalidDataException>(() => DirectRunAcquirer.ParseTimeline(timeline.RootElement));
    }

    [Fact]
    public void ParseJobAndCheckoutLogs_RequireOneFullGuidAndSha()
    {
        Assert.Equal("120a7f50-3d56-4411-bb20-1ec7b3ab753b",
            DirectRunAcquirer.ParseHelixJobId(
                "Sent https://helix.dot.net/api/jobs/120a7f50-3d56-4411-bb20-1ec7b3ab753b/workitems?access_token={redacted}"));
        Assert.Equal(new string('a', 40),
            DirectRunAcquirer.ParsePerformanceSha($"git checkout --force refs/remotes/origin/{new string('a', 40)}"));
        Assert.Throws<InvalidDataException>(() => DirectRunAcquirer.ParseHelixJobId("no job"));
    }

    [Fact]
    public void ParseWorkItems_PreservesPartialAndFailedPartitions()
    {
        using var status = JsonDocument.Parse("""
            {"totalWorkItems":4,
             "passed":[
               {"Name":"x64.micro_wasm.net11.0.Partition0","ExitCode":0},
               {"Name":"x64.micro_wasm.net11.0.Partition2","ExitCode":0},
               {"Name":"controller","ExitCode":0}],
             "failed":[{"Name":"x64.micro_wasm.net11.0.Partition1","ExitCode":1}]}
            """);

        var items = DirectRunAcquirer.ParseWorkItems(status.RootElement);

        Assert.Equal(["Partition0", "Partition1", "Partition2"], items.Select(item => item.Partition));
        Assert.Equal("failed", items[1].Status);
        Assert.Equal(1, items[1].ExitCode);
    }

    [Fact]
    public void ParseReportFiles_AllowsOnlyFullReportsFromKnownHosts()
    {
        using var files = JsonDocument.Parse("""
            {"other":[
              {"Name":"A-report-full.json","Uri":"https://safe.blob.core.windows.net/c/a?sig=secret"},
              {"Name":"Partition0-combined-perf-lab-report.json","Uri":"https://helix.dot.net/api/file"},
              {"Name":"console.log","Uri":"https://safe.blob.core.windows.net/c/b?sig=secret"},
              {"Name":"B-report-full.json","Uri":"https://evil.example/B-report-full.json?token=secret"}],
             "testResults":[],"binlogs":[]}
            """);

        var reports = DirectRunAcquirer.ParseReportFiles(files.RootElement);

        Assert.Equal("Partition0-combined-perf-lab-report.json", Assert.Single(reports).Name);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(reports.Select(report => report.Name)));
    }

    [Fact]
    public async Task SnapshotExport_IsDeterministicAndRejectsSensitiveStrings()
    {
        var snapshot = Snapshot();
        var first = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json.gz");
        var second = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json.gz");
        try
        {
            await BuildSnapshotImporter.WriteAsync(snapshot, first);
            await BuildSnapshotImporter.WriteAsync(snapshot, second);
            Assert.Equal(await File.ReadAllBytesAsync(first), await File.ReadAllBytesAsync(second));
            await Assert.ThrowsAsync<InvalidDataException>(() => BuildSnapshotImporter.WriteAsync(
                snapshot with { Caveats = ["See https://helix.dot.net/?access_token=secret"] }, second));
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    [Fact]
    public void BuildSelector_OrdersNumericBuildIdsNewestFirst()
    {
        Assert.Equal(["3074629", "3074425", "3068640"],
            BuildSnapshotStoreOrdering.OrderBuildIds(["3068640", "local", "3074425", "3074629"]));
    }

    private static BuildSnapshot Snapshot()
    {
        var build = new BuildProvenance(
            "3074629", "20260913.1", new string('a', 40), new string('b', 40), "2026-09-13T15:04:49Z");
        var lanes = BuildComparison.LaneIds.Select(id =>
        {
            var measurement = new BuildMeasurement(
                new("Fixture", "Benchmarks", "Run", "N=1"), ["Fixture"],
                new(1, 1, 0, 0, 3, 1, 1, 0, [1, 1, 1]),
                "Partition0", "Fixture-report-full.json", null, 3, "IterationCount=3");
            return new BuildLane(
                new(id, id, Guid.Empty.ToString(), build, "11.0.0-ci", "15.2", "11.0.100",
                    "RunKind=micro", 15),
                [new("Partition0", "passed", "", 1, 1, 0)], [measurement]);
        }).ToArray();
        return new(1, build, lanes, [], "Direct Helix snapshot",
            DateTimeOffset.Parse("2026-09-13T20:30:00Z"));
    }
}
