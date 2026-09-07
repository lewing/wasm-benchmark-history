using System.Text.Json;
using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class BuildComparisonTests
{
    [Fact]
    public void Import_MatchesStructuredIdentityAndParametersNotJobIds()
    {
        var lanes = BuildComparison.LaneIds.Select((id, index) =>
            Lane(id, Parse($$$"""
                {"HostEnvironmentInfo":{"MachineName":"private-machine"},"Benchmarks":[
                  {"Namespace":"Fixture","Type":"Generic<Int32>","Method":"Run","Parameters":"N=1",
                   "FullName":"different-full-name-{{{index}}}","Job":{"Id":"lane-{{{index}}}"},
                   "Statistics":{"Mean":{{{16 / (index + 1.0)}}},"N":5,"StandardDeviation":2,"StandardError":0.5}},
                  {"Namespace":"Fixture","Type":"Generic<Int32>","Method":"Run","Parameters":"N=2",
                   "Statistics":{"Mean":8,"N":3}}
                ]}
                """))).ToArray();

        var result = BuildComparison.Analyze(Snapshot(lanes));

        Assert.Equal(2, result.Common.Length);
        Assert.All(result.Coverage, coverage => Assert.Equal(2, coverage.Valid));
        var first = result.Rows[0].Cells[BuildComparison.LaneIds[0]].ValidMeasurement!;
        Assert.Equal(2, first.Statistics.StandardDeviation);
        Assert.Equal(0.5, first.Statistics.StandardError);
        Assert.Equal(5, first.Statistics.N);
        var serialized = JsonSerializer.Serialize(result.Snapshot, BuildSnapshotImporter.JsonOptions);
        Assert.DoesNotContain("private-machine", serialized);
        Assert.DoesNotContain("different-full-name", serialized);
        Assert.DoesNotContain("lane-0", serialized);
    }

    [Fact]
    public void Analyze_AccountsForMissingInvalidAndDuplicateWithoutChoosingAWinner()
    {
        var good = Measurement("good", 8);
        var missing = Measurement("missing", 5);
        var invalid = Measurement("invalid", 4);
        var duplicate = Measurement("duplicate", 3);
        var lanes = BuildComparison.LaneIds.Select((id, index) => Lane(id,
            index == 0
                ? [good, invalid with { Statistics = invalid.Statistics with { Mean = 0 } }, duplicate, duplicate]
                : [good, missing, invalid, duplicate])).ToArray();
        var result = BuildComparison.Analyze(Snapshot(lanes));

        Assert.Single(result.Common);
        Assert.Equal("good", result.Common[0].Identity.Method);
        var coverage = result.Coverage[0];
        Assert.Equal(4, coverage.Entries);
        Assert.Equal(3, coverage.Unique);
        Assert.Equal(1, coverage.Missing);
        Assert.Equal(1, coverage.Invalid);
        Assert.Equal(1, coverage.Duplicates);
        Assert.Equal(1, coverage.Valid);
        Assert.All(BuildComparison.Summarize(result.Rows), pair => Assert.Equal(1, pair.Count));
    }

    [Fact]
    public void Summarize_UsesFourWayIntersectionAndLogGeomeanForAllSixPairs()
    {
        var lanes = BuildComparison.LaneIds.Select((id, index) => Lane(id,
            Measurement("one", index == 0 ? 8 : 2),
            Measurement("two", index == 0 ? 2 : 8),
            Measurement("extreme", index == 0 ? 1e250 : 1e249))).ToArray();
        var rows = BuildComparison.Analyze(Snapshot(lanes)).Rows;
        var pairs = BuildComparison.Summarize(rows);

        Assert.Equal(6, pairs.Length);
        Assert.All(pairs, pair => Assert.Equal(3, pair.Count));
        Assert.Equal(Math.Pow(10, 1.0 / 3), pairs[0].GeometricMean!.Value, 10);
        Assert.Equal(1, pairs[^1].GeometricMean!.Value, 10);
        var subset = BuildComparison.Summarize(rows.Where(row => row.Identity.Method == "one"));
        Assert.Equal(4, subset[0].GeometricMean!.Value, 10);
    }

    [Fact]
    public void Summarize_EmptySetHasNoInventedRatio()
    {
        Assert.All(BuildComparison.Summarize([]), pair =>
        {
            Assert.Equal(0, pair.Count);
            Assert.Null(pair.GeometricMean);
        });
    }

    [Theory]
    [InlineData(null, 5)]
    [InlineData(0.0, 5)]
    [InlineData(-1.0, 5)]
    [InlineData(double.NaN, 5)]
    [InlineData(double.PositiveInfinity, 5)]
    [InlineData(1.0, 0)]
    [InlineData(1.0, null)]
    public void InvalidMeasurements_AreExcluded(double? mean, int? n)
    {
        Assert.NotNull(BuildComparison.InvalidReason(new(mean, null, null, null, n, null, null)));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("""{"Mean":0,"N":3}""")]
    [InlineData("""{"Mean":"NaN","N":3}""")]
    [InlineData("""{"Mean":2,"N":1.5}""")]
    [InlineData("""{"Mean":2,"N":3,"StandardDeviation":"Infinity"}""")]
    [InlineData("""{"Mean":2,"N":3,"StandardError":-1}""")]
    public void Import_PreservesInvalidIdentities(string statistics)
    {
        var values = Parse($$"""
            {"Benchmarks":[{"Namespace":"N","Type":"T","Method":"M","Parameters":"","Statistics":{{statistics}}}]}
            """);
        Assert.NotNull(Assert.Single(values).InvalidReason);
    }

    [Fact]
    public void Import_ReportsUnidentifiedAndRejectsUnsupportedSchema()
    {
        using var document = JsonDocument.Parse("""
            {"Benchmarks":[{"FullName":"cannot infer identity","Statistics":{"Mean":1,"N":2}}]}
            """);
        Assert.Empty(BuildSnapshotImporter.ParseReport(document.RootElement, "Partition1", "file.json", out var count));
        Assert.Equal(1, count);
        using var unsupported = JsonDocument.Parse("""{"Results":[]}""");
        Assert.Throws<InvalidDataException>(() =>
            BuildSnapshotImporter.ParseReport(unsupported.RootElement, "Partition1", "file.json", out _));
    }

    [Fact]
    public void Import_AcceptsNullGlobalNamespaceButNotMissingNamespace()
    {
        var values = Parse("""
            {"Benchmarks":[{"Namespace":null,"Type":"ByteMark","Method":"BenchLUDecomp","Parameters":"",
                "Statistics":{"Mean":1,"N":2}}]}
            """);
        Assert.Equal("", Assert.Single(values).Identity.Namespace);
        using var document = JsonDocument.Parse("""
            {"Benchmarks":[{"Type":"ByteMark","Method":"BenchLUDecomp","Parameters":"",
                "Statistics":{"Mean":1,"N":2}}]}
            """);
        Assert.Empty(BuildSnapshotImporter.ParseReport(document.RootElement, "Partition1", "file.json", out var count));
        Assert.Equal(1, count);
    }

    [Theory]
    [InlineData("""{"IsCritical":true,"Message":"private diagnostics"}""", true)]
    [InlineData("""{"IsCritical":false,"Message":"noncritical warning"}""", false)]
    public void Import_ExcludesCriticalValidationWithoutRetainingRawMessages(string validation, bool invalid)
    {
        var values = Parse($$$"""
            {"ValidationErrors":[{{{validation}}}],"Benchmarks":[
              {"Namespace":"N","Type":"T","Method":"M","Parameters":"","Statistics":{"Mean":1,"N":2}}]}
            """);
        Assert.Equal(invalid, Assert.Single(values).InvalidReason is not null);
        Assert.DoesNotContain("private diagnostics", JsonSerializer.Serialize(values));
    }

    [Fact]
    public void Import_BenchmarkErrorsExcludeOtherwiseValidStatistics()
    {
        var values = Parse("""
            {"Benchmarks":[{"Namespace":"N","Type":"T","Method":"M","Parameters":"",
              "Statistics":{"Mean":1,"N":2},"Errors":["Execution failed on private-machine"]}]}
            """);
        Assert.NotNull(Assert.Single(values).InvalidReason);
        Assert.DoesNotContain("private-machine", JsonSerializer.Serialize(values));
    }

    [Fact]
    public void Import_PreservesSamplesVarianceAndAllStageMeasurementCount()
    {
        var values = Parse("""
            {"Benchmarks":[{"Namespace":"N","Type":"T","Method":"M","Parameters":"",
              "Statistics":{"Mean":2,"N":3,"OriginalValues":[1,2,3],"Variance":1},
              "DisplayInfo":"T.M: private-job(EvaluateOverhead=True, IterationTime=250ms, Host=private-machine, WarmupCount=1) [IterationCount=99]",
              "Measurements":[{},{},{},{},{}]}]}
            """);
        var value = Assert.Single(values);
        Assert.Equal(new double[] { 1, 2, 3 }, value.Statistics.OriginalValues);
        Assert.Equal(1, value.Statistics.Variance);
        Assert.Equal(5, value.MeasurementCount);
        Assert.Equal("EvaluateOverhead=True; IterationTime=250ms; WarmupCount=1", value.MeasurementConfiguration);
        Assert.DoesNotContain("private-", JsonSerializer.Serialize(value));
        Assert.Null(value.InvalidReason);
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("""[1,"NaN",3]""")]
    public void Import_RejectsMismatchedOrNonfiniteRetainedSamples(string samples)
    {
        var values = Parse($$$"""
            {"Benchmarks":[{"Namespace":"N","Type":"T","Method":"M","Parameters":"",
              "Statistics":{"Mean":2,"N":3,"OriginalValues":{{{samples}}}}}]}
            """);
        Assert.NotNull(Assert.Single(values).InvalidReason);
    }

    [Fact]
    public void Identity_DoesNotHaveDelimiterCollisions()
    {
        Assert.NotEqual(new BenchmarkIdentity("a.b", "c", "d", "").Key,
            new BenchmarkIdentity("a", "b.c", "d", "").Key);
        Assert.NotEqual(new BenchmarkIdentity("a", "b", "c", "x=1").Key,
            new BenchmarkIdentity("a", "b", "c", "x=2").Key);
    }

    [Fact]
    public void Analyze_RejectsMixedBuildsAndRepeatedLanesOrPartitions()
    {
        var snapshot = Snapshot(BuildComparison.LaneIds.Select(id => Lane(id, Measurement("one", 1))).ToArray());
        var first = snapshot.Lanes[0];
        var mixed = first with { Provenance = first.Provenance with
            { Build = snapshot.Build with { BuildId = "other" } } };
        Assert.Throws<InvalidDataException>(() => BuildComparison.Analyze(snapshot with
            { Lanes = [mixed, .. snapshot.Lanes.Skip(1)] }));
        Assert.Throws<InvalidDataException>(() => BuildComparison.Analyze(snapshot with
            { Lanes = [first, first, .. snapshot.Lanes.Skip(2)] }));
        Assert.Throws<InvalidDataException>(() => BuildComparison.Analyze(snapshot with
            { Lanes = [first with { Partitions = [first.Partitions[0], first.Partitions[0]] }, .. snapshot.Lanes.Skip(1)] }));
    }

    [Fact]
    public async Task Snapshot_CompressedRoundTripPreservesStatisticsAndCoverage()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json.gz");
        var snapshot = Snapshot(BuildComparison.LaneIds.Select(id => Lane(id, Measurement("one", 1))).ToArray());
        try
        {
            await BuildSnapshotImporter.WriteAsync(snapshot, path);
            var loaded = await BuildSnapshotImporter.ReadAsync(path);
            Assert.Equal(snapshot.Build, loaded.Build);
            Assert.Single(BuildComparison.Analyze(loaded).Common);
            Assert.Equal(snapshot.Lanes[0].Measurements[0].Statistics, loaded.Lanes[0].Measurements[0].Statistics);
            Assert.Equal(snapshot.Lanes[0].Partitions[0], loaded.Lanes[0].Partitions[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ImportManifest_RecoversUploadFailureAndCountsMissingReportPartition()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wasm-import-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var reportPath = Path.Combine(directory, "report.json");
        var manifestPath = Path.Combine(directory, "manifest.json");
        try
        {
            await File.WriteAllTextAsync(reportPath, """
                {"Benchmarks":[{"Namespace":"Fixture","Type":"T","Method":"M","Parameters":"",
                 "Statistics":{"Mean":2,"N":5,"StandardDeviation":1,"StandardError":0.4}}]}
                """);
            var lanes = BuildComparison.LaneIds.Select(id => new ImportLane(
                Lane(id).Provenance with { ExpectedPartitions = 3 },
                [new("Partition1", id == "coreclr-r2r" ? "upload-failed" : "passed",
                    id == "coreclr-r2r" ? "Complete reports recovered after upload failure." : "",
                    ["report.json"]),
                 new("Partition2", "missing-reports", "No result artifact was available.", [])])).ToArray();
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(
                new BuildImportManifest(Build, lanes, []), BuildSnapshotImporter.JsonOptions));

            var snapshot = await BuildSnapshotImporter.ImportAsync(manifestPath);
            var comparison = BuildComparison.Analyze(snapshot);
            Assert.Single(comparison.Common);
            Assert.All(comparison.Coverage, coverage =>
            {
                Assert.Equal(2, coverage.Partitions);
                Assert.Equal(3, coverage.ExpectedPartitions);
                Assert.Equal(1, coverage.Valid);
            });
            Assert.Equal("upload-failed", snapshot.Lanes[3].Partitions[0].Status);

            lanes[0] = lanes[0] with { Partitions = [new("Partition1", "passed", "", ["../outside.json"])] };
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(
                new BuildImportManifest(Build, lanes, []), BuildSnapshotImporter.JsonOptions));
            await Assert.ThrowsAsync<InvalidDataException>(() => BuildSnapshotImporter.ImportAsync(manifestPath));
        }
        finally
        {
            File.Delete(reportPath);
            File.Delete(manifestPath);
            Directory.Delete(directory);
        }
    }

    private static BuildMeasurement[] Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var result = BuildSnapshotImporter.ParseReport(document.RootElement, "Partition1", "report.json", out var rejected);
        Assert.Equal(0, rejected);
        return result;
    }

    private static readonly BuildProvenance Build = new(
        "123", "20260907.2", new string('a', 40), new string('b', 40), "2026-09-07");

    private static BuildMeasurement Measurement(string method, double mean) =>
        new(new("Fixture", "Benchmarks", method, ""), ["Fixture"], new(mean, mean, 0, 0, 3, mean, mean),
            "Partition1", "report.json", null);

    private static BuildLane Lane(string id, params BuildMeasurement[] values) =>
        new(new(id, id, Guid.Empty.ToString(), Build, "11", "14", "11", "Throughput", 1),
            [new("Partition1", "passed", "", 1, values.Length, 0)], values);

    private static BuildSnapshot Snapshot(BuildLane[] lanes) => new(1, Build, lanes, []);
}
