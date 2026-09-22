namespace WasmBenchmarkHistory.Data;

public sealed class ChartSelectionState
{
    public DateTime? TargetTimestamp { get; private set; }

    public DateTime? PinnedTimestamp { get; private set; }

    public DateTime? DisplayTimestamp => PinnedTimestamp ?? TargetTimestamp;

    public bool IsPinned => PinnedTimestamp is not null;

    public void Hover(DateTime timestamp)
    {
        if (!IsPinned)
        {
            TargetTimestamp = timestamp;
        }
    }

    public void Scrub(DateTime timestamp)
    {
        TargetTimestamp = timestamp;
        if (IsPinned)
        {
            PinnedTimestamp = timestamp;
        }
    }

    public void Pin(DateTime timestamp)
    {
        TargetTimestamp = timestamp;
        PinnedTimestamp = timestamp;
    }

    public void TogglePin()
    {
        if (PinnedTimestamp is not null)
        {
            PinnedTimestamp = null;
        }
        else if (TargetTimestamp is not null)
        {
            PinnedTimestamp = TargetTimestamp;
        }
    }

    public void Unpin(bool clearTarget)
    {
        PinnedTimestamp = null;
        if (clearTarget)
        {
            TargetTimestamp = null;
        }
    }

    public void Leave(bool retainTransientSelection)
    {
        if (!IsPinned && !retainTransientSelection)
        {
            TargetTimestamp = null;
        }
    }

    public void Reconcile(IReadOnlyCollection<DateTime> availableTimestamps)
    {
        if (PinnedTimestamp is not null
            && !availableTimestamps.Contains(PinnedTimestamp.Value))
        {
            PinnedTimestamp = null;
        }

        if (TargetTimestamp is not null
            && !availableTimestamps.Contains(TargetTimestamp.Value))
        {
            TargetTimestamp = null;
        }
    }

    public void Reset()
    {
        TargetTimestamp = null;
        PinnedTimestamp = null;
    }
}
