namespace WasmBenchmarkHistory.Data;

public sealed class InvestigationWorkflowState
{
    public string? RunId =>
        BaselinePin is not null && ComparisonPin is not null
            ? string.Equals(BaselinePin.RunId, ComparisonPin.RunId, StringComparison.Ordinal)
                ? BaselinePin.RunId
                : null
            : BaselinePin?.RunId ?? ComparisonPin?.RunId;

    public InvestigationPin? BaselinePin { get; private set; }

    public InvestigationPin? ComparisonPin { get; private set; }

    public bool HasComparison =>
        BaselinePin is not null
        && ComparisonPin is not null;

    public void Set(InvestigationPinRequest request)
    {
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
        InvestigationPin? baselinePin,
        InvestigationPin? comparisonPin)
    {
        Clear();
        BaselinePin = baselinePin;
        ComparisonPin = comparisonPin;
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

    }

    public void Clear()
    {
        BaselinePin = null;
        ComparisonPin = null;
    }
}
