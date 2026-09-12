using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class ChartSelectionStateTests
{
    private static readonly DateTime First = new(2026, 1, 1);
    private static readonly DateTime Second = First.AddDays(1);

    [Fact]
    public void HoverIsTransientUntilPinned()
    {
        var state = new ChartSelectionState();

        state.Hover(First);
        Assert.Equal(First, state.DisplayTimestamp);
        Assert.False(state.IsPinned);

        state.Leave(retainTransientSelection: false);
        Assert.Null(state.DisplayTimestamp);
    }

    [Fact]
    public void PointerPinStaysStableUntilAnotherTimestampIsPinned()
    {
        var state = new ChartSelectionState();

        state.Pin(First);
        state.Hover(Second);
        state.Leave(retainTransientSelection: false);
        Assert.Equal(First, state.DisplayTimestamp);

        state.Pin(Second);
        Assert.True(state.IsPinned);
        Assert.Equal(Second, state.PinnedTimestamp);
    }

    [Fact]
    public void KeyboardScrubbingMovesPinnedTimestamp()
    {
        var state = new ChartSelectionState();
        state.Scrub(First);
        state.TogglePin();

        state.Scrub(Second);

        Assert.True(state.IsPinned);
        Assert.Equal(Second, state.TargetTimestamp);
        Assert.Equal(Second, state.PinnedTimestamp);
    }

    [Fact]
    public void KeyboardToggleAndEscapeUnpinPreserveTransientTarget()
    {
        var state = new ChartSelectionState();
        state.Scrub(First);
        state.TogglePin();
        Assert.True(state.IsPinned);

        state.TogglePin();
        Assert.False(state.IsPinned);
        Assert.Equal(First, state.DisplayTimestamp);

        state.Pin(Second);
        state.Unpin(clearTarget: false);
        Assert.False(state.IsPinned);
        Assert.Equal(Second, state.DisplayTimestamp);
    }

    [Fact]
    public void ReconcileClearsSelectionOutsideNewTimeline()
    {
        var state = new ChartSelectionState();
        state.Pin(First);

        state.Reconcile([Second]);

        Assert.Null(state.DisplayTimestamp);
        Assert.False(state.IsPinned);
    }
}
