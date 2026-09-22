namespace WasmBenchmarkHistory.Data;

public sealed class ChartSeriesFocusState
{
    public string? FocusedRunId { get; private set; }

    public bool IsFocused(string runId) =>
        string.Equals(FocusedRunId, runId, StringComparison.Ordinal);

    public bool IsDimmed(string runId) =>
        FocusedRunId is not null && !IsFocused(runId);

    public void Toggle(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        FocusedRunId = IsFocused(runId) ? null : runId;
    }

    public void ShowAll() => FocusedRunId = null;

    public void Reconcile(IEnumerable<string> visibleRunIds)
    {
        ArgumentNullException.ThrowIfNull(visibleRunIds);
        if (FocusedRunId is not null
            && !visibleRunIds.Contains(FocusedRunId, StringComparer.Ordinal))
        {
            FocusedRunId = null;
        }
    }
}
