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

### Investigate a temporal regression

Use **30d**, **90d**, **1y**, or **All** to focus the chart, or drag across the
plot to create a custom zoom range. Open an observation detail card and pin one
observation from a run as **baseline A**, then another observation from that
same run as **comparison B**. Pins remain visible while inspecting other points.

The regression panel always presents the observations older-first, even when A
and B were selected in reverse. It reports the raw values/errors, absolute and
percentage change, ratio, trace/build names, full source identities, and safe
`dotnet/runtime` / `dotnet/performance` compare links when their SHAs differ.
A zero older value is reported as an undefined percentage/ratio rather than
infinity.

The optional robust summary uses up to seven observations around each pin (the
pin plus three neighbors on either side). It compares medians and reports the
interpolated 25th/75th percentiles and IQR with sample counts. It intentionally
does not claim statistical significance or calculate a p-value.

Benchmark, selected runs, chart mode/range, investigation run, summary method,
and exact A/B identities are encoded in the query string. Reloading or sharing
the URL restores valid state; malformed, unavailable, or ambiguous fields are
ignored with an on-page explanation.

## Architecture

- `Components/Pages/Home.razor` owns the catalog/search/comparison workflow.
- `Components/HistoryChart.razor` renders a responsive native SVG chart without
  a charting dependency. Its collocated JavaScript module keeps drag movement
  local and sends one completed range to the server.
- `Components/RegressionInvestigationPanel.razor` keeps within-run temporal
  analysis visually and semantically separate from strict runtime comparison.
- `Data/BenchmarkIndexParser.cs` catalogs links from the three small index
  pages. History pages are fetched only after a benchmark is selected.
- `Data/BenchmarkHistoryParser.cs` extracts the `defaultCounter` primary trace
  from generated JavaScript as text. It never evaluates downloaded JavaScript.
- `Data/ObservationMatcher.cs` joins observations only on timestamp, runtime
  SHA, and performance-repository SHA. There is no timestamp-only fallback.
- `Data/RegressionInvestigation.cs`, `HistoryTimeRange.cs`, and
  `HistoryPageState.cs` provide exact pin resolution, median/IQR windows,
  validated compare links, range resolution, and safe share-state parsing.
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

The only JavaScript owned by this app is the checked-in chart drag module. It
operates solely on local pointer coordinates and never receives or evaluates
content downloaded from benchmark pages.

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

Small checked-in HTML fixtures and focused unit tests cover encoded benchmark
names, primary-trace selection, optional errors, strict SHA matching, range
selection, reverse/zero/duplicate pin cases, median/IQR windows, compare-link
validation, and query-state round trips and rejection.

```bash
dotnet test tests/WasmBenchmarkHistory.Tests --filter 'Category!=Live'
RUN_LIVE_SMOKE=1 dotnet test tests/WasmBenchmarkHistory.Tests --filter 'Category=Live'
dotnet build WasmBenchmarkHistory.slnx
```

The live smoke test is read-only. It loads all three published indexes and one
shared benchmark history from each run configuration.
