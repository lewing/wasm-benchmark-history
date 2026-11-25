namespace WasmBenchmarkHistory.Data;

public sealed class BenchmarkSelectionState
{
    private readonly List<string> _selected = [];
    private string? _anchor;

    public IReadOnlyList<string> Selected => _selected;

    public string? ActiveBenchmark { get; private set; }

    public bool Contains(string benchmark) => _selected.Contains(benchmark, StringComparer.Ordinal);

    public void Select(
        string benchmark,
        IReadOnlyList<string> visibleBenchmarks,
        bool toggle = false,
        bool extend = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(benchmark);
        var anchorIndex = _anchor is null ? -1 : IndexOf(visibleBenchmarks, _anchor);
        var targetIndex = IndexOf(visibleBenchmarks, benchmark);
        if (extend && anchorIndex >= 0 && targetIndex >= 0)
        {
            if (!toggle)
            {
                _selected.Clear();
            }

            for (var index = Math.Min(anchorIndex, targetIndex); index <= Math.Max(anchorIndex, targetIndex); index++)
            {
                Add(visibleBenchmarks[index]);
            }
        }
        else
        {
            if (!toggle)
            {
                _selected.Clear();
            }

            if (!toggle || !_selected.Remove(benchmark))
            {
                Add(benchmark);
            }
            _anchor = benchmark;
        }

        ActiveBenchmark = Contains(benchmark) ? benchmark : _selected.LastOrDefault();
    }

    public void Activate(string benchmark)
    {
        if (!Contains(benchmark))
        {
            throw new ArgumentException("The active benchmark must be selected.", nameof(benchmark));
        }

        ActiveBenchmark = benchmark;
    }

    public void Restore(IEnumerable<string> benchmarks, string? activeBenchmark)
    {
        _selected.Clear();
        foreach (var benchmark in benchmarks)
        {
            Add(benchmark);
        }

        ActiveBenchmark = activeBenchmark is not null && Contains(activeBenchmark)
            ? activeBenchmark
            : _selected.FirstOrDefault();
        _anchor = ActiveBenchmark;
    }

    private void Add(string benchmark)
    {
        if (!Contains(benchmark))
        {
            _selected.Add(benchmark);
        }
    }

    private static int IndexOf(IReadOnlyList<string> benchmarks, string benchmark)
    {
        for (var index = 0; index < benchmarks.Count; index++)
        {
            if (string.Equals(benchmarks[index], benchmark, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}
