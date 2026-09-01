namespace WasmBenchmarkHistory.Data;

public sealed record RunConfiguration(
    string Id,
    string DisplayName,
    Uri IndexUri,
    string Color,
    string Description);

public static class KnownRunConfigurations
{
    private const string BaseUrl = "https://pvscmdupload.z22.web.core.windows.net";

    public static IReadOnlyList<RunConfiguration> All { get; } =
    [
        new(
            "mono-wasm",
            "Mono WASM",
            new Uri(BaseUrl + "/reports/allTestHistory/refs/heads/main_x64_ubuntu%2022.04_CompilationMode=wasm_RunKind=micro/ViperUbuntu/AllTestindex.html"),
            "#4f7cff",
            "Mono interpreter/JIT WASM microbenchmarks"),
        new(
            "mono-wasm-aot",
            "Mono WASM AOT",
            new Uri(BaseUrl + "/reports/allTestHistory/refs/heads/main_x64_ubuntu%2022.04_AOT=true_CompilationMode=wasm_RunKind=micro/ViperUbuntu/AllTestindex.html"),
            "#f4a340",
            "Mono ahead-of-time compiled WASM microbenchmarks"),
        new(
            "coreclr-wasm",
            "CoreCLR WASM",
            new Uri(BaseUrl + "/reports/allTestHistory/refs/heads/main_x64_ubuntu%2022.04_CompilationMode=wasm_RunKind=micro_RuntimeType=coreclr/ViperUbuntu/AllTestindex.html"),
            "#20b486",
            "CoreCLR WASM microbenchmarks")
    ];

    public static RunConfiguration Get(string id) =>
        All.FirstOrDefault(run => run.Id == id)
        ?? throw new ArgumentException($"Unknown run configuration '{id}'.", nameof(id));
}

public sealed record BenchmarkLink(string Benchmark, Uri PageUri);

public sealed class BenchmarkCatalogEntry(
    string benchmark,
    IReadOnlyDictionary<string, Uri> pages)
{
    public string Benchmark { get; } = benchmark;

    public IReadOnlyDictionary<string, Uri> Pages { get; } = pages;

    public bool IsShared => Pages.Count == KnownRunConfigurations.All.Count;

    public string Availability =>
        IsShared
            ? "Shared by all runs"
            : $"Missing: {string.Join(", ", KnownRunConfigurations.All
                .Where(run => !Pages.ContainsKey(run.Id))
                .Select(run => run.DisplayName))}";
}

public sealed class BenchmarkCatalog
{
    private readonly IReadOnlyDictionary<string, BenchmarkCatalogEntry> _byName;

    public BenchmarkCatalog(IEnumerable<BenchmarkCatalogEntry> entries)
    {
        Entries = entries.OrderBy(entry => entry.Benchmark, StringComparer.OrdinalIgnoreCase).ToArray();
        _byName = Entries.ToDictionary(entry => entry.Benchmark, StringComparer.Ordinal);
    }

    public IReadOnlyList<BenchmarkCatalogEntry> Entries { get; }

    public BenchmarkCatalogEntry? Find(string benchmark) =>
        _byName.GetValueOrDefault(benchmark);
}

public sealed record BenchmarkObservation(
    string Benchmark,
    string RunId,
    DateTime Timestamp,
    double Value,
    double? Error,
    string RuntimeSha,
    string PerformanceSha,
    string? TraceName);

public sealed record BenchmarkHistory(
    string Benchmark,
    RunConfiguration Run,
    string? TraceName,
    IReadOnlyList<BenchmarkObservation> Observations);

public readonly record struct ObservationKey(
    DateTime Timestamp,
    string RuntimeSha,
    string PerformanceSha);

public sealed record MatchedComparison(
    ObservationKey Key,
    IReadOnlyDictionary<string, BenchmarkObservation> Observations);

public enum BenchmarkDataError
{
    Network,
    Schema,
    UnknownBenchmark
}

public sealed class BenchmarkDataException(
    BenchmarkDataError error,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public BenchmarkDataError Error { get; } = error;
}
