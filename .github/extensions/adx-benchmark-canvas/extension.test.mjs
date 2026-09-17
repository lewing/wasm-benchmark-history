import assert from "node:assert/strict";
import { mkdtemp, readFile, rm, writeFile, mkdir } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import {
    appendDeduplicatedRows,
    buildRatioHistogram,
    derivePairs,
    filterRows,
    formatDuration,
    selectDurationUnit,
    stableRowKey,
} from "./analysis.mjs";
import {
    MAX_ROWS,
    normalizeAnalysis,
    normalizeRows,
    validateSafeUrl,
    ValidationError,
} from "./schema.mjs";
import {
    AnalysisMutationQueue,
    AnalysisStore,
    createEmptyState,
    shouldImportArtifact,
} from "./state.mjs";

function row(overrides = {}) {
    return {
        benchmark: { id: "System.Tests.Perf_Random.Next_int_unseeded", name: "Perf_Random.Next_int_unseeded", family: "System.Tests" },
        build: { timestamp: "2026-09-13T14:59:42Z", name: "20260913.1", id: "3074629" },
        series: { id: "coreclr-interpreter", label: "CoreCLR interpreter", runId: "run-a" },
        valueNs: 2_530,
        stddevNs: 125,
        runtimeSha: "3c4631e63b1de4308e2965b149992b992c9f5318",
        performanceSha: "743c3c623a09044b4833d2c2766bb77b4c8cce8e",
        sourceUrl: "https://github.com/dotnet/performance/blob/main/src/benchmarks/micro/System/Tests/Perf_Random.cs",
        historyUrl: "https://pvscmdupload.z22.web.core.windows.net/reports/allTestHistory/example.html",
        status: "valid",
        ...overrides,
    };
}

function analysis(overrides = {}) {
    return {
        version: 1,
        analysisId: "wasm-smoke",
        metadata: {
            title: "Wasm comparison",
            createdAt: "2026-09-15T18:00:00Z",
            querySummary: "Read-only normalized PerformanceData rows",
            dataSource: "PerformanceData",
        },
        chart: {
            visualization: "paired-scatter",
            baselineSeriesId: "coreclr-interpreter",
            selectedSeries: ["coreclr-interpreter", "coreclr-r2r"],
        },
        rows: [row()],
        ...overrides,
    };
}

test("normalizes the versioned canonical analysis", () => {
    const normalized = normalizeAnalysis(analysis());
    assert.equal(normalized.version, 1);
    assert.equal(normalized.rows[0].valueNs, 2_530);
    assert.equal(normalized.chart.thresholds.slowdownRatio, 1.05);
});

test("rejects unsafe URLs and credentials", () => {
    assert.throws(() => validateSafeUrl("http://example.com/data"), (error) =>
        error instanceof ValidationError && error.code === "unsafe_url");
    assert.throws(() => validateSafeUrl("https://token@example.com/data"), (error) =>
        error instanceof ValidationError && error.code === "unsafe_url");
    assert.throws(() => validateSafeUrl("https://localhost/data"), (error) =>
        error instanceof ValidationError && error.code === "unsafe_url");
    for (const url of [
        "https://127.0.0.1/private",
        "https://10.0.0.1/private",
        "https://169.254.1.1/private",
        "https://[::1]/private",
        "https://[fd00::1]/private",
    ]) {
        assert.throws(() => validateSafeUrl(url), (error) =>
            error instanceof ValidationError && error.code === "unsafe_url");
    }
});

test("stable key distinguishes build and run identity", () => {
    const first = normalizeRows([row()])[0];
    const second = normalizeRows([row({ series: { ...row().series, runId: "run-b" } })])[0];
    assert.notEqual(stableRowKey(first), stableRowKey(second));
});

test("append replaces exact stable keys and preserves other rows", () => {
    const existing = normalizeRows([
        row(),
        row({ build: { ...row().build, timestamp: "2026-09-12T14:59:42Z", name: "20260912.6" } }),
    ]);
    const replacement = row({ valueNs: 2_400 });
    const result = appendDeduplicatedRows(existing, [replacement]);
    assert.equal(result.rows.length, 2);
    assert.equal(result.added, 0);
    assert.equal(result.replaced, 1);
    assert.equal(result.rows.find((item) => item.build.name === "20260913.1").valueNs, 2_400);
});

test("adaptive units match the Blazor duration thresholds", () => {
    assert.equal(selectDurationUnit(0.0318), "ps");
    assert.equal(selectDurationUnit(31.8), "ns");
    assert.equal(selectDurationUnit(2_530), "µs");
    assert.equal(selectDurationUnit(44_300_000), "ms");
    assert.equal(selectDurationUnit(1_270_000_000), "s");
    assert.equal(formatDuration(2_530, "µs"), "2.53\u00a0µs");
});

test("derives strict pairs and ratio histograms", () => {
    const rows = normalizeRows([
        row(),
        row({ series: { id: "coreclr-r2r", label: "CoreCLR R2R", runId: "run-b" }, valueNs: 500 }),
    ]);
    const pairs = derivePairs(rows, "coreclr-interpreter", "coreclr-r2r");
    assert.equal(pairs.length, 1);
    assert.equal(pairs[0].ratio, 500 / 2_530);
    const histogram = buildRatioHistogram(pairs, 5);
    assert.equal(histogram.bins.reduce((sum, bin) => sum + bin.count, 0), 1);
});

test("filters by series, text, status, and time range", () => {
    const rows = normalizeRows([
        row(),
        row({
            benchmark: { id: "Other.Case", name: "Other case", family: "Other" },
            build: { timestamp: "2026-08-01T00:00:00Z", name: "old" },
            series: { id: "coreclr-r2r", label: "CoreCLR R2R" },
            status: "invalid",
        }),
    ]);
    const filtered = filterRows(rows, {
        selectedSeries: ["coreclr-interpreter"],
        filters: { search: "random", statuses: ["valid"] },
        range: { from: "2026-09-01T00:00:00Z", to: "2026-10-01T00:00:00Z" },
    });
    assert.equal(filtered.length, 1);
});

test("stores durable state by analysisId", async () => {
    const root = await mkdtemp(path.join(os.tmpdir(), "adx-canvas-test-"));
    try {
        const store = new AnalysisStore(root);
        const first = createEmptyState("analysis-one");
        first.analysis = normalizeAnalysis(analysis({ analysisId: "analysis-one" }));
        first.view = first.analysis.chart;
        first.revision = 3;
        await store.save(first);
        await store.save(createEmptyState("analysis-two"));
        assert.equal((await store.load("analysis-one")).analysis.metadata.title, "Wasm comparison");
        assert.equal((await store.load("analysis-two")).analysis, null);
        assert.match(store.stateArtifactPath("analysis-one"), /analysis-one\.json$/);
    } finally {
        await rm(root, { recursive: true });
    }
});

test("imports an open artifact only when durable analysis is empty", () => {
    assert.equal(shouldImportArtifact(createEmptyState("empty-analysis")), true);
    const state = createEmptyState("loaded-analysis");
    state.analysis = normalizeAnalysis(analysis({ analysisId: "loaded-analysis" }));
    assert.equal(shouldImportArtifact(state), false);
});

test("serializes concurrent mutations for the same analysis", async () => {
    const queue = new AnalysisMutationQueue();
    const order = [];
    let releaseFirst;
    const firstGate = new Promise((resolve) => {
        releaseFirst = resolve;
    });
    const first = queue.run("analysis", async () => {
        order.push("first-start");
        await firstGate;
        order.push("first-end");
    });
    const second = queue.run("analysis", async () => {
        order.push("second");
    });
    await new Promise((resolve) => setImmediate(resolve));
    assert.deepEqual(order, ["first-start"]);
    releaseFirst();
    await Promise.all([first, second]);
    assert.deepEqual(order, ["first-start", "first-end", "second"]);
});

test("reads only session-relative canonical artifacts", async () => {
    const root = await mkdtemp(path.join(os.tmpdir(), "adx-canvas-artifact-"));
    try {
        const store = new AnalysisStore(root);
        await mkdir(path.join(root, "files"), { recursive: true });
        await writeFile(path.join(root, "files", "input.json"), JSON.stringify(analysis()), "utf8");
        assert.equal((await store.readArtifact("files/input.json")).analysisId, "wasm-smoke");
        await assert.rejects(() => store.readArtifact("../outside.json"), (error) =>
            error.code === "invalid_artifact_path");
    } finally {
        await rm(root, { recursive: true });
    }
});

test("rejects NaN, infinity, unknown fields, and oversized rows", () => {
    assert.throws(() => normalizeAnalysis(analysis({ rows: [row({ valueNs: Number.NaN })] })), /finite number/);
    assert.throws(() => normalizeAnalysis(analysis({ rows: [row({ valueNs: Number.POSITIVE_INFINITY })] })), /finite number/);
    assert.throws(() => normalizeAnalysis(analysis({ unexpected: true })), /not supported/);
    const oversized = Array.from({ length: MAX_ROWS + 1 }, () => row());
    assert.throws(() => normalizeAnalysis(analysis({ rows: oversized })), (error) =>
        error instanceof ValidationError && error.code === "oversized_input");
});

test("persisted JSON never changes exact numeric values", async () => {
    const root = await mkdtemp(path.join(os.tmpdir(), "adx-canvas-exact-"));
    try {
        const store = new AnalysisStore(root);
        const state = createEmptyState("exact-numbers");
        state.analysis = normalizeAnalysis(analysis({
            analysisId: "exact-numbers",
            rows: [row({ valueNs: 360052.73437500006, rawSamplesNs: [360052.73437500006] })],
        }));
        state.view = state.analysis.chart;
        await store.save(state);
        const raw = await readFile(path.join(root, store.stateArtifactPath("exact-numbers")), "utf8");
        assert.match(raw, /360052\.73437500006/);
    } finally {
        await rm(root, { recursive: true });
    }
});
