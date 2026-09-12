namespace WasmBenchmarkHistory.Data;

public sealed class InvestigationWorkflowState
{
    public string? RunId { get; private set; }

    public InvestigationPin? BaselinePin { get; private set; }

    public InvestigationPin? ComparisonPin { get; private set; }

    public bool HasComparison =>
        RunId is not null
        && BaselinePin is not null
        && ComparisonPin is not null;

    public void Set(InvestigationPinRequest request)
    {
        if (!string.Equals(RunId, request.Pin.RunId, StringComparison.Ordinal))
        {
            Clear();
            RunId = request.Pin.RunId;
        }

        if (request.Slot == InvestigationPinSlot.BaselineA)
        {
            BaselinePin = request.Pin;
        }
        else
        {
            ComparisonPin = request.Pin;
        }
    }

    public void Restore(
        string? runId,
        InvestigationPin? baselinePin,
        InvestigationPin? comparisonPin)
    {
        Clear();
        if (runId is null)
        {
            return;
        }

        RunId = runId;
        BaselinePin = baselinePin?.RunId == runId ? baselinePin : null;
        ComparisonPin = comparisonPin?.RunId == runId ? comparisonPin : null;
    }

    public void Clear(InvestigationPinSlot slot)
    {
        if (slot == InvestigationPinSlot.BaselineA)
        {
            BaselinePin = null;
        }
        else
        {
            ComparisonPin = null;
        }

        if (BaselinePin is null && ComparisonPin is null)
        {
            RunId = null;
        }
    }

    public void Clear()
    {
        RunId = null;
        BaselinePin = null;
        ComparisonPin = null;
    }
}
