using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class ChartSeriesFocusStateTests
{
    [Fact]
    public void Toggle_FocusesSelectedSeriesAndDimsOthers()
    {
        var state = new ChartSeriesFocusState();

        state.Toggle("coreclr-wasm-r2r");

        Assert.True(state.IsFocused("coreclr-wasm-r2r"));
        Assert.False(state.IsDimmed("coreclr-wasm-r2r"));
        Assert.True(state.IsDimmed("mono-wasm"));
    }

    [Fact]
    public void ToggleFocusedSeriesOrShowAll_ResetsFocus()
    {
        var state = new ChartSeriesFocusState();
        state.Toggle("coreclr-wasm-r2r");

        state.Toggle("coreclr-wasm-r2r");
        Assert.Null(state.FocusedRunId);

        state.Toggle("mono-wasm");
        state.ShowAll();
        Assert.Null(state.FocusedRunId);
    }

    [Fact]
    public void Reconcile_ClearsFocusWhenSeriesIsNoLongerVisible()
    {
        var state = new ChartSeriesFocusState();
        state.Toggle("coreclr-wasm-r2r");

        state.Reconcile(["mono-wasm", "coreclr-wasm"]);

        Assert.Null(state.FocusedRunId);
    }
}
