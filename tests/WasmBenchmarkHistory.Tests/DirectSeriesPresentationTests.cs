using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class DirectSeriesPresentationTests
{
    [Fact]
    public void BuildConnectorPaths_JoinsConsecutiveDirectObservations()
    {
        var start = new DateTime(2026, 9, 1);
        var points = new[]
        {
            new Point(start, "10,20"),
            new Point(start.AddHours(6), "30,40"),
            new Point(start.AddHours(12), "50,60")
        };

        var path = Assert.Single(DirectSeriesPresentation.BuildConnectorPaths(
            points,
            point => point.Timestamp,
            point => point.Coordinates));

        Assert.Equal("10,20 30,40 50,60", path);
    }

    [Fact]
    public void BuildConnectorPaths_BreaksAcrossLongGaps()
    {
        var start = new DateTime(2026, 9, 1);
        var points = new[]
        {
            new Point(start, "10,20"),
            new Point(start.AddHours(6), "30,40"),
            new Point(start.AddDays(8), "50,60"),
            new Point(start.AddDays(8).AddHours(6), "70,80")
        };

        var paths = DirectSeriesPresentation.BuildConnectorPaths(
            points,
            point => point.Timestamp,
            point => point.Coordinates);

        Assert.Equal(["10,20 30,40", "50,60 70,80"], paths);
    }

    [Fact]
    public void BuildConnectorPaths_LeavesOneObservationAsMarkerOnly()
    {
        var points = new[]
        {
            new Point(new DateTime(2026, 9, 1), "10,20")
        };

        Assert.Empty(DirectSeriesPresentation.BuildConnectorPaths(
            points,
            point => point.Timestamp,
            point => point.Coordinates));
    }

    [Fact]
    public void CreateMarker_DescribesShapeSourceValueAndGuideSemantics()
    {
        var marker = DirectSeriesPresentation.CreateMarker(
            "CoreCLR Wasm R2R (direct snapshots)",
            new DateTime(2026, 9, 13, 14, 59, 42),
            "156.7 ms");

        Assert.Equal("direct-marker", marker.CssClass);
        Assert.Equal("img", marker.Role);
        Assert.Equal("diamond", marker.Shape);
        Assert.Contains("discrete direct snapshot", marker.AccessibleLabel);
        Assert.Contains("156.7 ms", marker.AccessibleLabel);
        Assert.Contains("visual guide only", marker.AccessibleLabel);
    }

    private sealed record Point(DateTime Timestamp, string Coordinates);
}
