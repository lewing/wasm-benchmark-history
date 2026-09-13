# Wasm benchmark history

[![CI](https://github.com/lewing/wasm-benchmark-history/actions/workflows/ci.yml/badge.svg)](https://github.com/lewing/wasm-benchmark-history/actions/workflows/ci.yml)
[![Live data smoke](https://github.com/lewing/wasm-benchmark-history/actions/workflows/live-smoke.yml/badge.svg)](https://github.com/lewing/wasm-benchmark-history/actions/workflows/live-smoke.yml)

A narrowly scoped, server-hosted Blazor app for comparing historical .NET Wasm
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
Hover and keyboard scrubbing are read-only previews. Click or tap a timestamp,
or press Enter/Space while the chart scrubber is focused, to pin the card and
reveal A/B actions. A pinned card remains stable while the pointer moves; click
another timestamp to move it, or press Escape/use its close button to unpin.
Choosing **Set baseline A** or **Set comparison B** automatically returns the
chart to selection mode and focuses the keyboard scrubber, so the next point can
be previewed and pinned immediately. A short status message states which pin is
still needed.

### Investigate a temporal regression

Use **30d**, **90d**, **1y**, or **All** to focus the chart, or drag across the
plot to create a custom zoom range. Open an observation detail card and pin one
observation from a run as **baseline A**, then another observation from that
same run as **comparison B**. Pins remain visible while inspecting other points.
Enable **Variability analysis** to keep the combined linear chart as the
magnitude overview while adding one aligned small multiple per runtime. Each
small multiple has its own linear value axis and overlays a descriptive trailing
median with a Q1–Q3 band. A shared subplot reports relative volatility as
`100 × IQR / |rolling median|`, making dispersion comparable when runtimes
differ by orders of magnitude. The selectable 7, 15, and 31-observation windows
use only primary-series values; the default is 31 observations (roughly one
week at the current publication cadence).
To keep Blazor Server interaction responsive, passive hover updates only the
main chart and detail card. The heavier variability panels synchronize when a
timestamp is explicitly pinned.

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

Rolling variability panels are likewise descriptive context, not a confidence
interval or significance test. In normalized mode they are calculated from the
displayed strict-match ratios rather than mixing raw measurement units. Lines
break across unusually long publication gaps instead of implying continuous
measurement.

Benchmark, selected runs, chart mode/range, variability-band settings,
investigation run, summary method, and exact A/B identities are encoded in the
query string. Reloading or sharing the URL restores valid state; malformed,
unavailable, or ambiguous fields are ignored with an on-page explanation.

## Autofiled change-set prototype

Open **Change set** (`/change-set`) to import a public
[`dotnet/perf-autofiling-issues`](https://github.com/dotnet/perf-autofiling-issues)
issue. Accepted inputs are a bare issue number, an
`owner/repo#number` reference, or the canonical public issue URL. The prototype
intentionally rejects every repository except `dotnet/perf-autofiling-issues`,
does not follow HTTP redirects, and fetches GitHub data only from the server.

The importer reads hidden `DATA` metadata, repeated run-information sections,
improvement/regression groups, benchmark table rows, exact history/source/report
links, reported values, Test Quality, Edge Detector, and group repro commands.
It does not render issue HTML or Markdown. External triage is collapsed by
default and reduced to a plain-text summary plus allowlisted HTTPS links; it is
explicitly issue commentary rather than analysis produced by this app.

Rows receive a conservative report-fact heuristic:

- **strong reported signal**: absolute reported change is at least 10%,
  Test Quality is at most 0.05, and Edge Detector is false;
- **high variance/noise risk**: Test Quality is at least 0.15 or Edge Detector
  is true;
- **review**: every other reported result.

These categories are not statistical-significance claims. The thresholds live
in `ChangeSetSignalHeuristic` and have focused tests.

Every benchmark row initially includes a compact A→B slope sparkline derived
only from the issue's reported ratio. Each expanded family also offers
**Load history previews**, which fetches only that family's histories with
bounded concurrency and replaces the slope glyphs with sampled real
observations around the exact A/B boundary. Full numerical context still
requires the explicit per-benchmark **Analyze history** action, so opening a
change set never bulk-downloads all histories.

History enrichment is strictly on demand for one selected benchmark. The exact
allowlisted report URL is fetched through the existing disk cache and parsed as
text by `BenchmarkHistoryParser`; downloaded JavaScript is never executed. The
imported runtime SHAs (and performance SHA when supplied by metadata) must each
resolve to exactly one observation. Missing or ambiguous identities fail
visibly rather than falling back to a date or nearby SHA. Successful analysis
shows nearby and trailing median/IQR context, relative volatility, sample
counts, and a conservative stable/noisy/insufficient boundary label. It also
creates an existing benchmark-view URL carrying the exact run and A/B pins.

Unauthenticated GitHub REST requests are rate limited. Configure an optional
server-side token with `GitHub:Token` or the standard ASP.NET Core environment
key `GitHub__Token`; it is applied only to the server's GitHub `HttpClient`.
Request timeout is controlled by `GitHub:RequestTimeoutSeconds`:

```json
{
  "GitHub": {
    "Token": "",
    "RequestTimeoutSeconds": 20
  }
}
```

The prototype does not fetch all benchmark histories, display the issue's
static graph images, execute repro commands, query ADX, infer candidate commits,
or claim a root cause. It supports only the current public autofiling Markdown
shape and the known report host/run catalog. Schema changes, unsafe links,
unsupported runs, and exact-match failures are surfaced as errors.

## Four-runtime same-build comparison

Open **Four-runtime comparison** (`/compare-builds`) to compare imported results
for Mono interpreter, Mono AOT, CoreCLR interpreter, and CoreCLR R2R. This view
does not depend on public historical indexes having a ReadyToRun lane. It keeps
official same-build measurements separate from earlier one-iteration ColdStart
experiments and leaves the historical chart's strict matching unchanged.

The coverage table accounts for the union of benchmark identities. The six
pairwise geometric means all use the **same four-way common set**, excluding
missing, invalid, and duplicate identities. Identity is the exact BDN namespace,
type, method, and parameter string, never the BDN job ID or display name.
Parameter strings are not heuristically rewritten. Multiple reports for an
identity are flagged as duplicates, even if their means agree; no arbitrary
winner is selected. Records without a structured identity are counted as
unidentified, not guessed from `FullName`. BDN's valid `Namespace: null` means
the global namespace and is normalized to an empty string; a missing namespace
property is still rejected.

**Speedup = baseline mean / candidate mean**: above 1 means the candidate is
faster. Aggregates use the exponential of the average log difference, with
equal weight per benchmark/parameter combination. Choose a category to compute
the same comparison within that category. BDN categories are used when present;
otherwise categories are benchmark namespaces. Search, pagination, and the
coverage filter provide individual comparisons, with means in nanoseconds per
operation, standard deviation, standard error, and retained statistics sample
count. Category tags can overlap; their counts are not additive. Source
partitions and report names remain inspectable.

Only finite positive means with positive integer `Statistics.N` qualify.
Non-finite or negative variance statistics are invalid; absent variance remains
explicitly unavailable. BDN errors or critical validation failures exclude
affected report/benchmark entries even when statistics exist. Raw error messages
are not exported. Upload status does not determine measurement validity:
an upload-only failed partition can contain usable complete BDN reports.
Different machines, measurement counts, and benchmark variability limit the
interpretation of a single build. These descriptive speedups are not statistical
significance claims.

### Included snapshot: build 3068640

`DataSets/3068640.json.gz` contains build `20260907.2` from September 7, 2026:
all **60/60 benchmark partitions**, 11,100 full BDN reports, and 22,261 benchmark
entries. Their total measurement-record count is 1,449,717 (including warmup and
overhead stages, not just retained result samples). Of 5,685 identities in the
union, **5,237** have valid results in all four modes. There are no duplicates
or unidentified entries. R2R Partition1's 357 benchmark entries were recovered
despite its post-execution upload failure.

| Runtime | Valid | Invalid | Absent from lane |
|---|---:|---:|---:|
| Mono interpreter | 5,682 | 1 | 2 |
| Mono AOT | 5,288 | 2 | 395 |
| CoreCLR interpreter | 5,621 | 23 | 41 |
| CoreCLR R2R | 5,630 | 14 | 41 |

All 40 invalid entries have null Statistics; passed Helix partitions are not
equivalent to complete benchmark usability. The full JSON exporter has no error
or validation-error fields in this build, so the snapshot does not certify the
absence of console warnings.

| Baseline | Candidate | Common-set speedup |
|---|---|---:|
| Mono interpreter | Mono AOT | 3.488183x |
| Mono interpreter | CoreCLR interpreter | 0.041691x |
| Mono interpreter | CoreCLR R2R | 0.181274x |
| Mono AOT | CoreCLR interpreter | 0.011952x |
| Mono AOT | CoreCLR R2R | 0.051968x |
| CoreCLR interpreter | CoreCLR R2R | 4.348044x |

All six use exactly the same 5,237 cases, not per-pair intersections. The target
runtime build source is `2120018b13c80b3bfff680dcfa93bc2e668664cc`; the performance
source is `2d66af36f374db948edc29264dfe95829fa183bc`. V8 is `15.2.124`.
The target runtime package is `11.0.0-ci`, distinct from the host SDK
`11.0.100-rc.1.26431.109`. Lane configuration defines interpreter/AOT/R2R mode:
BDN target descriptions can misleadingly call interpreters "Wasm AOT".
Workload manifest `11.0.100-manifests.01db7193` is recorded; a distinct workload
source SHA is unavailable. Build provenance is not independent verification of
source hashes embedded in the target binaries.

Each partition's BDN host report identifies Ubuntu 22.04.5 LTS, x64,
AMD EPYC 9124, 16 physical cores / 32 logical cores. This matches hardware/OS
class, not host identity or identical execution conditions. Retained statistics
sample counts range from 12 to 20 in every lane. The usual exported settings
are overhead evaluation enabled, 250 ms iteration time, 15 minimum / 20 maximum
iterations, and one warmup. Some cases export `IterationCount=6` and
`WarmupCount=-1` (39 entries in each Mono lane, 33 in each CoreCLR lane);
per-benchmark settings preserve these exceptions rather than substituting a
uniform configuration.

### Import a build

The application includes a reusable local BenchmarkDotNet importer:

```bash
dotnet run --project src/WasmBenchmarkHistory -- \
  --import-build /absolute/private-cache/manifest.json \
  /absolute/repo/src/WasmBenchmarkHistory/DataSets/BUILD_ID.json.gz
```

The importer reads full BDN JSON reports (`Benchmarks` with structured identity
and `Statistics`), prints coverage and six speedups, and writes a compressed
allowlisted snapshot. `DataSets/*.json.gz` ship with build/publish output and
populate the build selector. Raw logs and acquisition caches must stay outside
the repository.

The manifest shape is:

```json
{
  "build": {
    "buildId": "BUILD_ID",
    "buildNumber": "BUILD_NUMBER",
    "runtimeSha": "FULL_40_CHARACTER_RUNTIME_SHA",
    "performanceSha": "FULL_40_CHARACTER_PERFORMANCE_SHA",
    "sourceDate": "YYYY-MM-DD"
  },
  "lanes": [
    {
      "provenance": {
        "id": "mono-interpreter",
        "displayName": "Mono interpreter",
        "helixJobId": "HELIX_JOB_GUID",
        "build": { "...": "repeat the exact build object above" },
        "runtimeVersion": "runtime version",
        "v8Version": "V8 version",
        "workloadVersion": "workload version",
        "configuration": "measurement/job configuration",
        "expectedPartitions": 15,
        "hostSdkVersion": "host SDK version (not the target runtime)",
        "installerSdkSha": "installer/SDK SHA (not the runtime SHA)",
        "benchmarkDotNetVersion": "BenchmarkDotNet version",
        "measurementConfiguration": "public-safe iteration/warmup configuration"
      },
      "partitions": [
        {
          "name": "Partition1",
          "status": "passed",
          "note": "",
          "reports": ["mono-interpreter/Partition1/Example-report-full.json"]
        }
      ]
    }
  ],
  "caveats": ["Public-safe hardware and measurement comparability notes."]
}
```

Supply all four lane IDs: `mono-interpreter`, `mono-aot`,
`coreclr-interpreter`, `coreclr-r2r`. Every lane must declare identical build
provenance (including both full SHAs). Report paths are relative to and must
remain inside the manifest directory. Enumerate every acquired partition;
missing partitions remain visible against `expectedPartitions`, and partitions
without reports must carry an explanatory note. Empty or incompatible report
schemas fail the import rather than returning success-shaped measurements.
Use absolute command-line paths because `dotnet run --project` starts the
application in the project directory. The output directory must already exist.
The four environment fields after `expectedPartitions` are optional; the UI
explicitly reports unavailable values. Numeric variance, original retained
sample values, and total measurement-record counts (including non-result
stages) are preserved when present, in addition to the displayed statistics.
An allowlist extracts numeric/enum measurement settings from BDN `DisplayInfo`
when the exporter omits a `Job` object; arbitrary job labels, paths, and
benchmark parameter text are not copied into this configuration field.

Provenance in BDN alone cannot prove source build identity: the acquisition
manifest must be assembled from the actual build's lane/job/partition inventory,
not by combining unrelated report directories. Review manifest strings before
importing. The exporter **constructs a new measurement schema**, rather than
copying arbitrary report fields or stripping a few sensitive keys. It does not
retain `HostEnvironmentInfo`, full job objects, logs, URLs, tokens, SAS queries,
machine names, accounts, or filesystem paths. Only sanitized measurements,
report basenames, and explicitly curated public-safe provenance belong in
checked-in snapshots. Internal data acquisition requires authorized access;
running the app does not.

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
- `Data/ChangeSetIssueParser.cs` treats autofiling issue Markdown as untrusted
  data, preserves exact benchmark identities, and allowlists external links.
- `Data/PerfAutofilingIssueClient.cs` performs server-side GitHub REST requests
  with redirect rejection, timeout/token configuration, and explicit errors.
- `Data/ChangeSetHistoryAnalysis.cs` maps one imported history URL into the
  existing parser and robust investigation model without loading the catalog.
- `Components/Pages/ChangeSet.razor` presents the grouped change set, evidence
  drawer, report-fact heuristic, external triage summary, and exact deep links.
- `Data/RollingVariabilityCalculator.cs` computes trailing rolling medians and
  IQR bands locally from primary-series values.
- `Data/CachedPageClient.cs` and `DiskPageCache.cs` provide bounded-refresh
  local caching with stale-cache fallback during network failures.
- `Data/BuildSnapshotImporter.cs` allowlists BDN statistics and identity into
  compressed same-build snapshots; `BuildComparison.cs` accounts for coverage
  and calculates strict common-set speedups.
- `Components/Pages/CompareBuilds.razor` presents the four-runtime snapshot
  comparison independently of the historical data source.

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
validation, query-state round trips and rejection, autofiling input validation,
hidden metadata, grouped Markdown tables, entity decoding, tricky parameters,
safe-link rejection, repro extraction, external-triage sanitization, heuristic
thresholds, exact history mapping, and missing/ambiguous SHA behavior.

```bash
dotnet test tests/WasmBenchmarkHistory.Tests --filter 'Category!=Live'
RUN_LIVE_SMOKE=1 dotnet test tests/WasmBenchmarkHistory.Tests --filter 'Category=Live'
RUN_LIVE_CHANGE_SET_SMOKE=1 dotnet test tests/WasmBenchmarkHistory.Tests --filter 'FullyQualifiedName~LiveChangeSetSmokeTests'
dotnet build WasmBenchmarkHistory.slnx
```

The live smoke test is read-only. It loads all three published indexes and one
shared benchmark history from each run configuration. GitHub Actions runs the
ordinary restore, Release build, tests, and publish validation for pull requests
and pushes to `main`. A separate daily/manual workflow runs the network-dependent
live smoke so upstream availability does not gate ordinary changes. The
change-set smoke separately imports public issue 79296 and analyzes one exact
history on demand.
