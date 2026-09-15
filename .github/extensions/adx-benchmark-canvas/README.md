# ADX Benchmark Visualizer canvas

`adx-benchmark-viz` is an agent-driven visualization workbench for normalized
`PerformanceData` results. It is not an ADX client and does not replace the
shareable Blazor application. The foreground Copilot agent executes
authenticated, read-only Kusto queries with an available ADX/Kusto tool, writes
exact numeric results to a session-workspace artifact, and then opens or updates
this canvas. The extension never requests credentials and the iframe never
accepts or executes KQL.

## Agent workflow

1. Query `PerformanceData` with the foreground agent's authenticated, read-only
   ADX/Kusto tool.
2. Normalize bounded results into the version-1 JSON contract below and write
   them under the session workspace, for example
   `files/adx-benchmark-canvas/input/coreclr-20260913.1.json`.
3. Open `adx-benchmark-viz` with a stable `analysisId` and optional
   `dataArtifactPath`.
4. Refine the existing analysis with `set_analysis`, `append_rows`, `set_view`,
   or `select_benchmark`. Use `get_state` for a concise summary.

Persistent state is keyed by `analysisId` at
`files/adx-benchmark-canvas/analyses/<analysisId>.json`; `instanceId` identifies
only a transient panel. JSON/CSV exports of the filtered view are written under
`files/adx-benchmark-canvas/exports/`.

## Version 1 data contract

```json
{
  "version": 1,
  "analysisId": "coreclr-wasm-20260913-1",
  "metadata": {
    "title": "CoreCLR Wasm R2R vs interpreter",
    "description": "Default timing counters for build 20260913.1",
    "createdAt": "2026-09-15T18:00:00Z",
    "querySummary": "Read-only PerformanceData query; normalized to nanoseconds",
    "dataSource": "PerformanceData",
    "sourceUrl": "https://..."
  },
  "chart": {
    "title": "CoreCLR Wasm R2R vs interpreter",
    "subtitle": "Candidate / baseline; lower duration is better",
    "visualization": "paired-scatter",
    "xSemantic": "baseline-value",
    "ySemantic": "candidate-value",
    "unitKind": "duration-ns",
    "filters": {
      "search": "",
      "families": [],
      "categories": [],
      "statuses": ["valid"],
      "buildNames": ["20260913.1"]
    },
    "selectedSeries": ["coreclr-interpreter", "coreclr-r2r"],
    "baselineSeriesId": "coreclr-interpreter",
    "thresholds": { "slowdownRatio": 1.05, "speedupRatio": 0.95 },
    "topN": 50,
    "logScale": true,
    "range": {}
  },
  "rows": [
    {
      "benchmark": {
        "id": "System.Tests.Perf_Random.Next_int_unseeded",
        "name": "Perf_Random.Next_int_unseeded",
        "family": "System.Tests",
        "category": "System"
      },
      "build": {
        "timestamp": "2026-09-13T14:59:42Z",
        "name": "20260913.1",
        "id": "3074629",
        "marker": "runtime build"
      },
      "series": {
        "id": "coreclr-interpreter",
        "label": "CoreCLR interpreter",
        "runId": "stable ADX run/configuration identity"
      },
      "valueNs": 2530.125,
      "stddevNs": 125,
      "stderrNs": 31.25,
      "medianNs": 2500,
      "q1Ns": 2450,
      "q3Ns": 2600,
      "rawSamplesNs": [2490, 2530.125, 2570],
      "runtimeSha": "3c4631e63b1de4308e2965b149992b992c9f5318",
      "performanceSha": "743c3c623a09044b4833d2c2766bb77b4c8cce8e",
      "runConfiguration": {
        "CompilationMode": "wasm",
        "RunKind": "micro",
        "RuntimeMode": "CoreCLR interpreter"
      },
      "sourceUrl": "https://github.com/dotnet/performance/blob/main/src/benchmarks/micro/...",
      "historyUrl": "https://...",
      "validity": true,
      "status": "valid"
    }
  ]
}
```

The stable deduplication key is the tuple:

`benchmark.id + build.timestamp + (build.id || build.name) + series.id + series.runId`.

Limits are 5,000 rows, 200 raw samples per row, 20,000 raw samples total, 32
selected series, 500 table rows, 24 run-configuration entries, 512 characters
for ordinary text, and 2,048 characters for URLs. Numbers must be finite;
durations and samples must be non-negative. URLs must be absolute public HTTPS
URLs without embedded credentials. Unknown fields and unsupported versions are
rejected with stable error codes.

## KQL and mapping examples

The snippets intentionally run in the foreground agent, not the extension.
`PerformanceData` deployments can expose slightly different flattened column
names, so first use the ADX tool's schema/introspection operation and adapt the
marked projections without changing the canonical JSON mapping.

### CoreCLR Wasm R2R vs interpreter for `20260913.1`

```kusto
let build = "20260913.1";
let interp = Measurements
| where BuildName == build
| where RunConfigurationsCompilationMode == "wasm"
    and RunConfigurationsRunKind == "micro"
    and tostring(RunConfigurations.RuntimeType) == "coreclr"
    and isempty(RunConfigurationsR2RType)
| where TestCounterDefaultCounter and TestCounterHigherIsBetter == false
| project TestName, TestCounterName,
          InterpNs=TestCounterResultAverage,
          InterpStdDev=TestCounterResultStdDev,
          InterpStdErr=TestCounterResultStdErr,
          InterpSamples=TestCounterResults,
          BuildTimeStamp, BuildGitHash, RunPerfRepoHash, RunWorkItemName;
let r2r = Measurements
| where BuildName == build
| where RunConfigurationsCompilationMode == "wasm"
    and RunConfigurationsRunKind == "micro"
    and tostring(RunConfigurations.RuntimeType) == "coreclr"
    and RunConfigurationsR2RType == "r2r"
| where TestCounterDefaultCounter and TestCounterHigherIsBetter == false
| project TestName, TestCounterName,
          R2RNs=TestCounterResultAverage,
          R2RStdDev=TestCounterResultStdDev,
          R2RStdErr=TestCounterResultStdErr,
          R2RSamples=TestCounterResults;
interp
| join kind=inner r2r on TestName, TestCounterName
| where isfinite(InterpNs) and isfinite(R2RNs) and InterpNs > 0 and R2RNs > 0
| extend Ratio=R2RNs/InterpNs
| top 250 by Ratio desc
```

Map two normalized series rows per strict pair: `coreclr-interpreter` and
`coreclr-r2r`. `BuildTimeStamp` maps to `build.timestamp`, `BuildGitHash` to
`runtimeSha`, `RunPerfRepoHash` to `performanceSha`, and `RunWorkItemName` can
contribute to `series.runId`. Keep `Measurements.TestCounterResults` raw sample
arrays only when bounded; otherwise omit `rawSamplesNs`. Render initially as
`paired-scatter`, then use `set_view` for `ratio-distribution` and
`ranked-table`.

### Multi-build time series for one benchmark

```kusto
let builds = dynamic(["20260907.2","20260908.3","20260908.6","20260908.8",
                      "20260912.5","20260912.6","20260913.1"]);
Measurements
| where BuildName in (builds)
| where TestName == "System.Tests.Perf_Random.Next_int_unseeded"
| where RunConfigurationsCompilationMode == "wasm"
    and RunConfigurationsRunKind == "micro"
    and TestCounterDefaultCounter
| extend RuntimeType=tostring(RunConfigurations.RuntimeType),
         R2RType=tostring(RunConfigurations.R2RType),
         AOT=tostring(RunConfigurations.AOT)
| extend SeriesId=case(
    R2RType == "r2r", "coreclr-r2r",
    RuntimeType == "coreclr", "coreclr-interpreter",
    AOT == "true", "mono-aot",
    "mono-interpreter")
| project Benchmark=TestName, BuildName, BuildTimeStamp, SeriesId,
          ValueNs=TestCounterResultAverage,
          StdDevNs=TestCounterResultStdDev,
          StdErrNs=TestCounterResultStdErr,
          Samples=TestCounterResults,
          RuntimeSha=BuildGitHash,
          PerformanceSha=RunPerfRepoHash,
          RunConfigurations
| order by BuildTimeStamp asc, SeriesId asc
```

Use stable runtime/run-configuration identities for `series.id`, preserve exact
UTC timestamps, and render as `time-series`. If the query provides median/Q1/Q3
or raw samples, map those fields to the optional band/sample properties.

### Top slowdown distribution grouped by family

```kusto
let baseline =
    Measurements
    | where BuildName == "20260913.1"
    | where RunConfigurationsCompilationMode == "wasm"
        and RunConfigurationsRunKind == "micro"
        and tostring(RunConfigurations.RuntimeType) == "coreclr"
        and isempty(RunConfigurationsR2RType)
    | where TestCounterDefaultCounter and TestCounterHigherIsBetter == false
    | project TestName, TestCounterName, BaselineNs=TestCounterResultAverage;
let candidate =
    Measurements
    | where BuildName == "20260913.1"
    | where RunConfigurationsCompilationMode == "wasm"
        and RunConfigurationsRunKind == "micro"
        and tostring(RunConfigurations.RuntimeType) == "coreclr"
        and RunConfigurationsR2RType == "r2r"
    | where TestCounterDefaultCounter and TestCounterHigherIsBetter == false
    | project TestName, TestCounterName, CandidateNs=TestCounterResultAverage;
baseline
| join kind=inner candidate on TestName, TestCounterName
| where BaselineNs > 0
| extend Ratio = CandidateNs / BaselineNs,
         Family = tostring(split(TestName, ".")[0])
| top 500 by Ratio desc
| summarize Benchmarks=count(), MedianRatio=percentile(Ratio, 50),
            P95Ratio=percentile(Ratio, 95) by Family
| order by P95Ratio desc
```

For the canvas, retain the underlying paired benchmark rows (not only aggregate
family summaries), set `benchmark.family`, and render `ratio-distribution` or
`ranked-table` with family filters. Ratios are derived from exact canonical
nanoseconds in the extension.

`Measurements.TestCounterResults` contains source raw samples in deployments
that retain them. Source links may require a separate lookup in
`AutoFileTestCounters`; when that table/relationship is unavailable, set
`sourceUrl` only from an allowlisted `https://github.com/dotnet/performance/...`
result obtained through GitHub code search and clearly label it as `code search`
rather than an exact source in `metadata.querySummary`. A source lookup can use:

```kusto
AutoFileTestCounters
| where isnotempty(TestSourceUrl)
| summarize arg_max(IngestTime, TestSourceUrl) by TestName
| project TestName, ExactSourceUrl=TestSourceUrl
```

## Actions

- `set_analysis`: full contract object or `artifactPath`.
- `append_rows`: bounded strict rows; replaces duplicate stable keys.
- `set_view`: visualization, title/subtitle, filters, selected series,
  baseline, thresholds, top-N, time range, and log scale.
- `select_benchmark`: benchmark/build/optional series pin.
- `get_state`: concise summary only.
- `clear_analysis`: clears the analysis under the stable ID.

All action handlers return raw result values and throw `CanvasError` with stable
codes. Open instances receive changes over SSE. Closing a panel stops its
loopback-only server.
