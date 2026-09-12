using WasmBenchmarkHistory.Data;

namespace WasmBenchmarkHistory.Tests;

public sealed class InvestigationWorkflowStateTests
{
    private static readonly RunConfiguration Run = KnownRunConfigurations.All[0];
    private static readonly BenchmarkObservation First = Observation(0, 10);
    private static readonly BenchmarkObservation Second = Observation(1, 12);

    [Fact]
    public void BaselineThenComparisonCompletesAnalysisAfterTransientReselection()
    {
        var history = History();
        var selection = new ChartSelectionState();
        var workflow = new InvestigationWorkflowState();

        selection.Hover(First.Timestamp);
        selection.Pin(First.Timestamp);
        workflow.Set(Request(InvestigationPinSlot.BaselineA, First));
        selection.Unpin(clearTarget: true);

        selection.Hover(Second.Timestamp);
        selection.Pin(Second.Timestamp);
        workflow.Set(Request(InvestigationPinSlot.ComparisonB, Second));

        Assert.True(workflow.HasComparison);
        Assert.Equal(First.Timestamp, workflow.BaselinePin!.Identity.Timestamp);
        Assert.Equal(Second.Timestamp, workflow.ComparisonPin!.Identity.Timestamp);
        Assert.NotNull(RegressionInvestigation.Analyze(
            history,
            workflow.BaselinePin,
            workflow.ComparisonPin));
    }

    [Fact]
    public void ComparisonThenBaselineCompletesAnalysis()
    {
        var history = History();
        var selection = new ChartSelectionState();
        var workflow = new InvestigationWorkflowState();

        selection.Pin(Second.Timestamp);
        workflow.Set(Request(InvestigationPinSlot.ComparisonB, Second));
        selection.Unpin(clearTarget: true);
        selection.Pin(First.Timestamp);
        workflow.Set(Request(InvestigationPinSlot.BaselineA, First));

        Assert.True(workflow.HasComparison);
        Assert.NotNull(RegressionInvestigation.Analyze(
            history,
            workflow.BaselinePin!,
            workflow.ComparisonPin!));
    }

    [Fact]
    public void ReplacingPinAndMovingTransientSelectionAreIndependent()
    {
        var selection = new ChartSelectionState();
        var workflow = new InvestigationWorkflowState();

        selection.Pin(First.Timestamp);
        workflow.Set(Request(InvestigationPinSlot.BaselineA, First));
        selection.Pin(Second.Timestamp);
        Assert.Equal(Second.Timestamp, selection.PinnedTimestamp);
        Assert.Equal(First.Timestamp, workflow.BaselinePin!.Identity.Timestamp);

        workflow.Set(Request(InvestigationPinSlot.BaselineA, Second));
        Assert.Equal(Second.Timestamp, workflow.BaselinePin!.Identity.Timestamp);
    }

    private static InvestigationPinRequest Request(
        InvestigationPinSlot slot,
        BenchmarkObservation observation) =>
        new(
            slot,
            new InvestigationPin(
                observation.RunId,
                ObservationIdentity.From(observation)));

    private static BenchmarkHistory History() =>
        new("Fixture", Run, "build", [First, Second]);

    private static BenchmarkObservation Observation(int day, double value) =>
        new(
            "Fixture",
            Run.Id,
            new DateTime(2026, 1, 1).AddDays(day),
            value,
            .1,
            $"{day + 10:x7}",
            $"{day + 100:x7}",
            $"build-{day}");
}
