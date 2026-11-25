using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class RunVisibilityStateTests
{
    [Fact]
    public void CycleAdvancesHiddenShownFocusedHidden()
    {
        var state = new RunVisibilityState();

        Assert.Equal(RunVisibility.Shown, state.Cycle("a"));
        Assert.Equal(RunVisibility.Focused, state.Cycle("a"));
        Assert.Equal(RunVisibility.Hidden, state.Cycle("a"));
        Assert.Empty(state.ShownRunIds);
        Assert.Empty(state.FocusedRunIds);
    }

    [Fact]
    public void OnlyNonFocusedRunsDimWhileAnyRunIsFocused()
    {
        var state = new RunVisibilityState();
        state.Reset(["a", "b", "c"]);
        Assert.False(state.IsDimmed("b"));

        state.Cycle("a");
        state.Cycle("c");

        Assert.False(state.IsDimmed("a"));
        Assert.True(state.IsDimmed("b"));
        Assert.False(state.IsDimmed("c"));
        Assert.Contains("a", state.ShownRunIds);

        state.ClearFocus();
        Assert.False(state.IsDimmed("b"));
        Assert.Equal(3, state.ShownRunIds.Count);
    }

    [Fact]
    public void ResetDropsFocusForRunsThatAreNotShown()
    {
        var state = new RunVisibilityState();

        state.Reset(["a", "b"], ["b", "z"]);

        Assert.Equal(["b"], state.FocusedRunIds);
        Assert.Equal(RunVisibility.Hidden, state.Get("z"));
    }
}
