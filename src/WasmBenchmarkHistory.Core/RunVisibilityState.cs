namespace WasmBenchmarkHistory.Data;

public enum RunVisibility
{
    Hidden,
    Shown,
    Focused
}

/// <summary>
/// Tristate run configuration model shared by the configuration list and the chart legend:
/// hidden runs are not loaded, shown runs are drawn, and focused runs are drawn solid while
/// every other shown run is drawn faintly. With no focused run, every shown run is solid.
/// </summary>
public sealed class RunVisibilityState
{
    private readonly HashSet<string> _shown = new(StringComparer.Ordinal);
    private readonly HashSet<string> _focused = new(StringComparer.Ordinal);

    public IReadOnlySet<string> ShownRunIds => _shown;

    public IReadOnlySet<string> FocusedRunIds => _focused;

    public RunVisibility Get(string runId) =>
        _focused.Contains(runId) ? RunVisibility.Focused
        : _shown.Contains(runId) ? RunVisibility.Shown
        : RunVisibility.Hidden;

    public bool IsDimmed(string runId) =>
        _focused.Count > 0 && !_focused.Contains(runId);

    public static RunVisibility Next(RunVisibility visibility) => visibility switch
    {
        RunVisibility.Hidden => RunVisibility.Shown,
        RunVisibility.Shown => RunVisibility.Focused,
        _ => RunVisibility.Hidden
    };

    /// <summary>Advances a run to its next state and returns the new state.</summary>
    public RunVisibility Cycle(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        var next = Next(Get(runId));
        Set(runId, next);
        return next;
    }

    public void Set(string runId, RunVisibility visibility)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        switch (visibility)
        {
            case RunVisibility.Hidden:
                _shown.Remove(runId);
                _focused.Remove(runId);
                break;
            case RunVisibility.Shown:
                _shown.Add(runId);
                _focused.Remove(runId);
                break;
            case RunVisibility.Focused:
                _shown.Add(runId);
                _focused.Add(runId);
                break;
        }
    }

    /// <summary>Replaces the state; focused runs that are not shown are dropped.</summary>
    public void Reset(IEnumerable<string> shown, IEnumerable<string>? focused = null)
    {
        ArgumentNullException.ThrowIfNull(shown);
        _shown.Clear();
        _focused.Clear();
        _shown.UnionWith(shown);
        if (focused is not null)
        {
            _focused.UnionWith(focused.Where(_shown.Contains));
        }
    }

    public void ClearFocus() => _focused.Clear();
}
