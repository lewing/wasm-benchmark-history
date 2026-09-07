using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class BuildSnapshotDataTests
{
    [Fact]
    public async Task Build3068640_ContainsCompleteInventoryAndReproducibleCommonSet()
    {
        var snapshot = await BuildSnapshotImporter.ReadAsync(
            Path.Combine(AppContext.BaseDirectory, "DataSets", "3068640.json.gz"));
        var result = BuildComparison.Analyze(snapshot);
        Assert.Equal("3068640", snapshot.Build.BuildId);
        Assert.Equal("2120018b13c80b3bfff680dcfa93bc2e668664cc", snapshot.Build.RuntimeSha);
        Assert.Equal(60, snapshot.Lanes.Sum(lane => lane.Partitions.Length));
        Assert.Equal(11100, snapshot.Lanes.Sum(lane => lane.Partitions.Sum(partition => partition.Reports)));
        Assert.Equal(22261, snapshot.Lanes.Sum(lane => lane.Measurements.Length));
        Assert.Equal(1449717, snapshot.Lanes.Sum(lane => lane.Measurements.Sum(value => value.MeasurementCount)));
        Assert.Equal(5685, result.Rows.Length);
        Assert.Equal(5237, result.Common.Length);
        Assert.Equal(new[] { 5682, 5288, 5621, 5630 }, result.Coverage.Select(value => value.Valid));
        Assert.Equal(new[] { 2, 395, 41, 41 }, result.Coverage.Select(value => value.Missing));
        Assert.Equal(new[] { 1, 2, 23, 14 }, result.Coverage.Select(value => value.Invalid));
        Assert.All(result.Coverage, value =>
        {
            Assert.Equal(0, value.Duplicates);
            Assert.Equal(0, value.Unidentified);
            Assert.Equal(15, value.Partitions);
            Assert.Equal(15, value.ExpectedPartitions);
        });
        var recovered = snapshot.Lanes.Single(lane => lane.Provenance.Id == "coreclr-r2r");
        Assert.Equal("recovered", recovered.Partitions.Single(partition => partition.Name == "Partition1").Status);
        Assert.Equal(357, recovered.Measurements.Count(value => value.Partition == "Partition1"));
        Assert.Contains(result.Common, row =>
            row.Cells["coreclr-r2r"].ValidMeasurement!.Partition == "Partition1");
        Assert.All(snapshot.Lanes.SelectMany(lane => lane.Measurements)
            .Where(value => value.InvalidReason is null), value =>
        {
            Assert.Equal(value.Statistics.N, value.Statistics.OriginalValues!.Length);
            Assert.NotNull(value.Statistics.StandardDeviation);
            Assert.NotNull(value.Statistics.StandardError);
        });
        var expectedRatios = new[]
        {
            3.4881833098688113, 0.04169094320568607, 0.18127404207052442,
            0.01195205053809339, 0.051968037791380074, 4.348043678843913
        };
        var pairs = BuildComparison.Summarize(result.Rows);
        for (var index = 0; index < expectedRatios.Length; index++)
        {
            Assert.Equal(5237, pairs[index].Count);
            Assert.Equal(expectedRatios[index], pairs[index].GeometricMean!.Value, 10);
        }
    }
}
