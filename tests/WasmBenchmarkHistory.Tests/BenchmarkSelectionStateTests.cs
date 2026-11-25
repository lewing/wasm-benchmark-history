using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class BenchmarkSelectionStateTests
{
    [Fact]
    public void ClickReplacesSelectionAndToggleAddsOrRemovesWithoutDuplicates()
    {
        var state = new BenchmarkSelectionState();
        string[] visible = ["A", "B", "C"];

        state.Select("A", visible);
        state.Select("B", visible, toggle: true);
        Assert.Equal(["A", "B"], state.Selected);
        Assert.Equal("B", state.ActiveBenchmark);

        state.Select("B", visible, toggle: true);
        Assert.Equal(["A"], state.Selected);
        Assert.Equal("A", state.ActiveBenchmark);

        state.Select("C", visible);
        Assert.Equal(["C"], state.Selected);
        state.Select("C", visible, toggle: true);
        Assert.Empty(state.Selected);
        Assert.Null(state.ActiveBenchmark);
    }

    [Fact]
    public void ShiftRangeUsesVisibleOrderAndKeepsItsAnchor()
    {
        var state = new BenchmarkSelectionState();
        string[] visible = ["A", "B", "C", "D"];
        state.Select("B", visible);

        state.Select("D", visible, extend: true);
        Assert.Equal(["B", "C", "D"], state.Selected);
        state.Select("A", visible, extend: true);
        Assert.Equal(["A", "B"], state.Selected);
        Assert.Equal("A", state.ActiveBenchmark);
    }

    [Fact]
    public void FilteredOutAnchorDoesNotSelectHiddenBenchmarks()
    {
        var state = new BenchmarkSelectionState();
        state.Select("A", ["A", "B", "C"]);
        state.Select("C", ["B", "C"], toggle: true, extend: true);

        Assert.Equal(["A", "C"], state.Selected);
        state.Activate("A");
        Assert.Equal(["A", "C"], state.Selected);
        Assert.Equal("A", state.ActiveBenchmark);
    }
}
