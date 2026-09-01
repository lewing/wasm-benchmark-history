namespace WasmBenchmarkHistory.Data;

public sealed class BenchmarkDataOptions
{
    public const string SectionName = "BenchmarkData";

    public string? CacheDirectory { get; set; }

    public int IndexCacheMinutes { get; set; } = 60;

    public int HistoryCacheMinutes { get; set; } = 1440;

    public int RequestTimeoutSeconds { get; set; } = 30;
}
