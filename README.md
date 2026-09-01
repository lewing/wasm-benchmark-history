# WASM benchmark history

A narrowly scoped, server-hosted Blazor app for comparing historical .NET WASM
microbenchmark results published by
[`dotnet/performance`](https://github.com/dotnet/performance).

## Run locally

The project targets the current template in this repository (`net11.0`).

```bash
dotnet restore WasmBenchmarkHistory.slnx
dotnet run --project src/WasmBenchmarkHistory
```

Open the URL printed by ASP.NET Core, search for a benchmark, select two or
three available run configurations, and choose **Compare histories**. The chart
can show complete raw histories or normalize strict matched observations to the
first selected run. Hover the chart to inspect the nearest timestamp, or focus
it and use the left/right arrow, Home, and End keys. The detail card reports
each run's value, build identities, trace name, and strict ratio when available.

## Architecture

- `Components/Pages/Home.razor` owns the catalog/search/comparison workflow.
- `Components/HistoryChart.razor` renders a responsive native SVG chart without
  a charting dependency.
- `Data/BenchmarkIndexParser.cs` catalogs links from the three small index
  pages. History pages are fetched only after a benchmark is selected.
- `Data/BenchmarkHistoryParser.cs` extracts the `defaultCounter` primary trace
  from generated JavaScript as text. It never evaluates downloaded JavaScript.
- `Data/ObservationMatcher.cs` joins observations only on timestamp, runtime
  SHA, and performance-repository SHA. There is no timestamp-only fallback.
- `Data/CachedPageClient.cs` and `DiskPageCache.cs` provide bounded-refresh
  local caching with stale-cache fallback during network failures.

## Data safety and errors

Index links are accepted only when they remain on the configured HTTPS host and
inside the decoded index directory. This preserves benchmark names containing
encoded or raw slashes while rejecting external links. Parsed arrays must be
aligned before observations are created. Network, schema, and unavailable
benchmark failures are surfaced in the UI.

The parser intentionally supports only the currently published literal shape:
the `defaultCounter` ID, its `trendData[...]` assignment, and the first object
in that array. A generator change such as computed properties, non-literal
arrays, a new timestamp format, or renamed fields will produce a schema error
rather than execute or guess at the data.

## Cache configuration

By default, cached HTML is stored outside the repository under the operating
system's local application-data directory in `wasm-benchmark-history/cache`.
Override it in `src/WasmBenchmarkHistory/appsettings.json` or environment
configuration:

```json
{
  "BenchmarkData": {
    "CacheDirectory": "/absolute/cache/path",
    "IndexCacheMinutes": 60,
    "HistoryCacheMinutes": 1440,
    "RequestTimeoutSeconds": 30
  }
}
```

An empty `CacheDirectory` keeps the platform default. `~` and environment
variables are expanded for configured paths.

## Tests

Small checked-in HTML fixtures cover encoded benchmark names, primary-trace
selection, optional errors, alignment failures, and strict SHA matching.

```bash
dotnet test tests/WasmBenchmarkHistory.Tests --filter 'Category!=Live'
RUN_LIVE_SMOKE=1 dotnet test tests/WasmBenchmarkHistory.Tests --filter 'Category=Live'
dotnet build WasmBenchmarkHistory.slnx
```

The live smoke test is read-only. It loads all three published indexes and one
shared benchmark history from each run configuration.
