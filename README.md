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

Open the URL printed by ASP.NET Core, search for a benchmark, select two to
four available run configurations, and choose **Compare histories**. The chart
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

All app-owned duration displays start from nanoseconds and use adaptive `ps`,
`ns`, `µs`, `ms`, or `s` units. Each chart axis keeps one stable unit across its
ticks; tables may adapt per value, with associated errors shown in the same
unit. Hover and detail views retain the exact canonical nanosecond value in
visible secondary text and accessible titles. Source-provided strings imported
from autofiling issues remain unchanged as evidence.

## Copilot ADX benchmark canvas

The project-scoped `adx-benchmark-viz` canvas is an agent-driven workbench for
fast, conversational visualization of normalized `PerformanceData` ADX results.
The foreground Copilot agent runs authenticated read-only Kusto queries, writes
the exact bounded data to a session-workspace JSON artifact, and opens the
canvas for time series, paired scatter, ratio distribution, or ranked-table
analysis. The extension does not request ADX credentials, execute KQL, or put
secrets/tokens in iframe state; the Blazor app remains the shareable product UI.

The versioned data contract, stable deduplication key, action reference,
validation limits, and example KQL/data mappings are documented in
[`.github/extensions/adx-benchmark-canvas/README.md`](.github/extensions/adx-benchmark-canvas/README.md).
Run its dependency-free tests with:

```bash
node --test .github/extensions/adx-benchmark-canvas/extension.test.mjs
```

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

All four Wasm configurations, including CoreCLR R2R, use public
`allTestHistory` indexes and generated benchmark-history pages as their primary
source. Installed direct Helix snapshots augment the catalog and history chart
only as an offline fallback for benchmarks already present in the corresponding
public index; fallback-only benchmark identities do not inflate catalog counts.
Published histories merge fallback points by the existing strict key
(`timestamp + runtime SHA + performance SHA`); an equivalent published point
wins, while a value or duplicate-cardinality conflict is surfaced instead of
silently selecting one. The **CoreCLR Wasm R2R** public index is
`CompilationMode=wasm_R2RType=r2r_RunKind=micro_RuntimeType=coreclr`.
Fallback-only points use diamond markers and dashed gap-aware guides, retain
their snapshot/build provenance in the hover card, and never replace or
interpolate published observations. Rolling variability still requires the
configured minimum observation window.

The bundled `DataSets/direct-history.json.gz` file is a compact offline
latest-build fallback, not the canonical history source. It is deterministically
projected from full sanitized snapshots and retains exact benchmark identity,
lane/build/partition provenance, mean, standard error, variance, sample count,
validity, runtime/performance SHAs, and measurement timestamp. Raw sample arrays
remain in full sanitized snapshots but are omitted from this bundled projection.
The current retention policy is the latest **7 verified complete builds**.

The current 6.6 MiB compact archive is about 80% smaller than the corresponding
seven full gzip snapshots and retains these measurement timestamps:

| Build | Build number | Timestamp (UTC) | Coverage note |
|---|---|---|---|
| 3068640 | `20260907.2` | 2026-09-07 09:35:50 | 60/60; one R2R upload-only failure recovered |
| 3069775 | `20260908.3` | 2026-09-08 16:00:24 | 60/60 passed |
| 3070008 | `20260908.6` | 2026-09-08 18:56:23 | 60/60; one CoreCLR interpreter artifact recovered |
| 3070235 | `20260908.8` | 2026-09-08 23:42:47 | 60/60 passed |
| 3074299 | `20260912.5` | 2026-09-12 19:04:14 | 60/60 passed |
| 3074425 | `20260912.6` | 2026-09-13 03:54:51 | 60/60 passed |
| 3074629 | `20260913.1` | 2026-09-13 14:59:42 | 60/60 passed |

Builds 3071174, 3071763, and 3072440 were inspected but excluded:
3071174 lacks usable artifacts for one Mono AOT and one CoreCLR interpreter
partition, while the latter two have empty R2R reports in all 15 partitions.

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
same-build calculations separate from earlier one-iteration ColdStart
experiments. Sanitized snapshot observations also augment the historical chart
through its existing strict matching model.

The coverage table accounts for the union of benchmark identities. Full BDN
reports use their structured namespace/type/method/parameter identity; combined
perf-lab reports use their exact canonical test name without heuristic
rewriting. The six
pairwise geometric means all use the **same four-way common set**, excluding
missing, invalid, and duplicate identities. Identity never uses a BDN job ID.
Multiple reports for an
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

### Included snapshots

#### Build 3074629

`DataSets/3074629.json.gz` is a **Direct Helix snapshot** of build
`20260913.1` from September 13, 2026. All four Wasm jobs completed all 15
benchmark partitions successfully:

| Runtime | Helix job | Valid | Invalid | Absent from lane |
|---|---|---:|---:|---:|
| Mono interpreter | `af4eb385-40ac-4b96-b97c-dc9d38abebdd` | 5,682 | 1 | 2 |
| Mono AOT | `7098afbe-560e-41e7-be39-ccaabf847d46` | 5,288 | 2 | 395 |
| CoreCLR interpreter | `9a1d9497-cd98-4464-ac0b-44cb55012a41` | 5,622 | 22 | 41 |
| CoreCLR R2R | `120a7f50-3d56-4411-bb20-1ec7b3ab753b` | 5,640 | 4 | 41 |

Of 5,685 identities in the union, **5,244** have valid results in all four
modes. There are no duplicates or unidentified entries. The target runtime SHA
is `3c4631e63b1de4308e2965b149992b992c9f5318`; the performance SHA is
`743c3c623a09044b4833d2c2766bb77b4c8cce8e`. Combined perf-lab reports preserve
the default top-counter samples, from which the snapshot computes mean, median,
sample variance, standard deviation, standard error, minimum, and maximum.
V8 and workload-manifest versions are not present in these combined artifacts
and remain explicitly unavailable rather than being inferred.

| Baseline | Candidate | Common-set speedup |
|---|---|---:|
| Mono interpreter | Mono AOT | 3.472112x |
| Mono interpreter | CoreCLR interpreter | 0.041090x |
| Mono interpreter | CoreCLR R2R | 0.269343x |
| Mono AOT | CoreCLR interpreter | 0.011834x |
| Mono AOT | CoreCLR R2R | 0.077573x |
| CoreCLR interpreter | CoreCLR R2R | 6.555008x |

#### Build 3068640

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

### Refresh from an internal build

The application includes a dependency-light acquisition command. It validates
the internal build host, pipeline definition/name, main branch, four expected
Wasm job names, Helix GUIDs, partition names, report hosts, report shapes, commit
SHAs, and output extension. It downloads only combined perf-lab or full BDN JSON
result artifacts and feeds them through the same allowlisted importer used by
the checked-in snapshots:

```bash
dotnet run --project src/WasmBenchmarkHistory -- \
  --acquire-build 'https://dev.azure.com/dnceng/internal/_build/results?buildId=BUILD_ID' \
  /absolute/repo/src/WasmBenchmarkHistory/DataSets/BUILD_ID.json.gz
```

Use `--discover-build URL_OR_ID` for a read-only job/partition inventory before
downloading. The command runs `dotnet dnx -y lewing.helix.mcp` from the system
temporary directory. Authenticate Azure DevOps for `dnceng/internal` and store
a valid `helix.dot.net` credential with that tool before running it. Helix
access is required; ADX is not used.

The raw cache defaults to the operating system's local application-data
directory under `wasm-benchmark-history/direct-runs/BUILD_ID`. Override it with
`--cache /absolute/private/cache`; repository-local cache paths are rejected.
The cache contains private source artifacts and must never be committed. The
tracked output is a new gzip JSON document constructed from an explicit
allowlist. It never copies machine/account IDs, correlation IDs, raw
`additionalData`, logs, binlogs, URLs, SAS queries, tokens, or credentials.
A final sensitive-string scan rejects prohibited internal content before write.

The exporter prints per-lane coverage and the six common-set speedups.
`DataSets/*.json.gz` ship with build/publish output, populate `/compare-builds`,
and contribute exact direct observations to the main benchmark explorer.
Refresh remains a manual maintainer operation after a successful build; this is
not an automatic replacement for the public allTestHistory pipeline.

To refresh the retained multi-build series and keep reusable full sanitized run
data outside the repository:

```bash
dotnet run --project src/WasmBenchmarkHistory -- \
  --refresh-direct-history \
  /absolute/repo/src/WasmBenchmarkHistory/DataSets/direct-history.json.gz \
  /absolute/private/full-snapshot-archive \
  7 \
  3068640 3069775 3070008 3070235 3071174 \
  3071763 3072440 3074299 3074425 3074629
```

The command discovers and validates each build, writes full allowlisted
snapshots (including retained sample arrays) to the configurable private archive,
then derives and replaces the compact checked-in series. Builds with incomplete
lane/partition artifact coverage are excluded explicitly; complete reports from
upload-only failed work items remain eligible. Retention is applied by
measurement timestamp, then build ID, so repeated refreshes order and prune the
same input deterministically.

Omit the build IDs to have the command query recent completed
`dotnet-runtime-perf` definition 702 main builds and continue newest-first until
it finds the requested number of complete runs:

```bash
dotnet run --project src/WasmBenchmarkHistory -- \
  --refresh-direct-history \
  /absolute/repo/src/WasmBenchmarkHistory/DataSets/direct-history.json.gz \
  /absolute/private/full-snapshot-archive \
  7
```

If full sanitized snapshots already exist, rebuild only the compact projection:

```bash
dotnet run --project src/WasmBenchmarkHistory -- \
  --build-direct-history \
  /absolute/repo/src/WasmBenchmarkHistory/DataSets/direct-history.json.gz \
  7 \
  /absolute/private/full-snapshot-archive/*.json.gz
```

The full-snapshot directory is intentionally outside the public repository.
`IBenchmarkHistoryProvider` keeps the explorer's exact fallback
merge/comparison semantics independent of snapshot storage.

For already acquired files, the lower-level manifest importer remains
available:

```bash
dotnet run --project src/WasmBenchmarkHistory -- \
  --import-build /absolute/private-cache/manifest.json \
  /absolute/repo/src/WasmBenchmarkHistory/DataSets/BUILD_ID.json.gz
```

It accepts either full BDN reports or combined perf-lab report arrays. The
manifest shape is:

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
  "caveats": ["Public-safe hardware and measurement comparability notes."],
  "captureSource": "Direct Helix snapshot",
  "capturedAt": "2026-09-13T23:30:00Z"
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
- `Data/BenchmarkIndexParser.cs` catalogs links from the four published index
  pages. `DirectSnapshotHistory.cs` adds valid fallback snapshot identities and
  merges exact observations only after a benchmark is selected.
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
- `Data/DirectRunAcquirer.cs` discovers the four AzDO/Helix lanes, validates
  authenticated result artifacts, and keeps its raw cache outside the repo.
- `Data/DirectHistoryArchive.cs` projects retained compact history from full
  sanitized snapshots, enforces completeness/retention, and exposes the bundled
  provider abstraction used by the explorer and cross-build trend.
- `Data/BuildSnapshotImporter.cs` allowlists BDN or combined perf-lab
  statistics and exact identity into compressed same-build snapshots;
  `BuildComparison.cs` accounts for coverage and calculates strict common-set
  speedups.
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

Small checked-in fixtures and focused unit tests cover encoded benchmark
names, primary-trace selection, optional errors, strict SHA matching, range
selection, reverse/zero/duplicate pin cases, median/IQR windows, compare-link
validation, query-state round trips and rejection, autofiling input validation,
hidden metadata, grouped Markdown tables, entity decoding, tricky parameters,
safe-link rejection, repro extraction, external-triage sanitization, heuristic
thresholds, exact history mapping, missing/ambiguous SHA behavior, AzDO job
discovery, lane mapping, partial partitions, combined perf-lab import,
deterministic export, sensitive-data rejection, snapshot selection, direct
catalog availability, source deduplication/conflicts, R2R fallback history,
four-way direct matching, and insufficient variability samples.

```bash
dotnet test tests/WasmBenchmarkHistory.Tests --filter 'Category!=Live'
RUN_LIVE_SMOKE=1 dotnet test tests/WasmBenchmarkHistory.Tests --filter 'Category=Live'
RUN_LIVE_CHANGE_SET_SMOKE=1 dotnet test tests/WasmBenchmarkHistory.Tests --filter 'FullyQualifiedName~LiveChangeSetSmokeTests'
dotnet build WasmBenchmarkHistory.slnx
```

The public live smoke test is read-only. It loads all four published indexes
and the supplied Fannkuch benchmark history from every published run
configuration, asserting that R2R has more than six observations. GitHub Actions runs the
ordinary restore, Release build, tests, and publish validation for pull requests
and pushes to `main`. A separate daily/manual workflow runs the network-dependent
live smoke so upstream availability does not gate ordinary changes. The
change-set smoke separately imports public issue 79296 and analyzes one exact
history on demand.
