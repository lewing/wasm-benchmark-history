import { CanvasError, createCanvas, joinSession } from "@github/copilot-sdk/extension";
import {
    appendDeduplicatedRows,
    derivePairs,
    filterRows,
    stateSummary,
} from "./analysis.mjs";
import {
    mergeAndNormalizeChart,
    normalizeAnalysis,
    normalizeRows,
    normalizeSelection,
    validateAnalysisId,
    ValidationError,
} from "./schema.mjs";
import { startCanvasServer } from "./server.mjs";
import {
    AnalysisMutationQueue,
    AnalysisStore,
    createEmptyState,
    shouldImportArtifact,
} from "./state.mjs";

const CANVAS_ID = "adx-benchmark-viz";
const instances = new Map();
const mutationQueue = new AnalysisMutationQueue();
let session;
let store;

function canvasError(error, fallbackCode = "analysis_error") {
    if (error instanceof CanvasError) {
        return error;
    }
    return new CanvasError(error?.code ?? (error instanceof ValidationError ? error.code : fallbackCode),
        error instanceof Error ? error.message : String(error));
}

function requireInstance(instanceId) {
    const entry = instances.get(instanceId);
    if (!entry) {
        throw new CanvasError("canvas_not_open", `Canvas instance '${instanceId}' is not open`);
    }
    return entry;
}

async function mutate(entry, mutation) {
    return mutationQueue.run(entry.analysisId, async () => {
        const current = await store.load(entry.analysisId);
        const next = await mutation(current);
        next.revision = current.revision + 1;
        const saved = await store.save(next);
        broadcastAnalysis(entry.analysisId, saved);
        return saved;
    });
}

function broadcastAnalysis(analysisId, state) {
    for (const entry of instances.values()) {
        if (entry.analysisId === analysisId) {
            entry.server.broadcast(state);
        }
    }
}

async function setAnalysis(ctx) {
    try {
        const entry = requireInstance(ctx.instanceId);
        const input = ctx.input ?? {};
        const analysis = input.analysis !== undefined
            ? normalizeAnalysis(input.analysis)
            : await store.readArtifact(input.artifactPath);
        if (analysis.analysisId !== entry.analysisId) {
            throw new CanvasError("analysis_id_mismatch",
                `Analysis '${analysis.analysisId}' cannot replace open analysis '${entry.analysisId}'; reopen the canvas with the new stable analysisId`);
        }
        const next = await mutate(entry, async (current) => ({
            ...current,
            analysis,
            view: analysis.chart,
            selection: null,
        }));
        return {
            analysisId: analysis.analysisId,
            rowCount: analysis.rows.length,
            revision: next.revision,
            stateArtifactPath: store.stateArtifactPath(analysis.analysisId),
        };
    } catch (error) {
        throw canvasError(error, "set_analysis_failed");
    }
}

async function appendRows(ctx) {
    try {
        const entry = requireInstance(ctx.instanceId);
        const additions = normalizeRows(ctx.input?.rows);
        let appendResult;
        const saved = await mutate(entry, async (current) => {
            if (!current.analysis) {
                throw new CanvasError("analysis_empty", "Load an analysis before appending rows");
            }
            appendResult = appendDeduplicatedRows(current.analysis.rows, additions);
            return {
                ...current,
                analysis: { ...current.analysis, rows: appendResult.rows },
            };
        });
        return {
            analysisId: saved.analysisId,
            rowCount: saved.analysis.rows.length,
            processed: additions.length,
            added: appendResult.added,
            replaced: appendResult.replaced,
            revision: saved.revision,
        };
    } catch (error) {
        throw canvasError(error, "append_rows_failed");
    }
}

async function setView(ctx) {
    try {
        const entry = requireInstance(ctx.instanceId);
        const saved = await mutate(entry, async (current) => ({
            ...current,
            view: mergeAndNormalizeChart(current.view, ctx.input ?? {}),
        }));
        return stateSummary(saved);
    } catch (error) {
        throw canvasError(error, "set_view_failed");
    }
}

async function selectBenchmark(ctx) {
    try {
        const entry = requireInstance(ctx.instanceId);
        const selection = normalizeSelection(ctx.input);
        const saved = await mutate(entry, async (current) => {
            if (selection && current.analysis && !current.analysis.rows.some((row) =>
                row.benchmark.id === selection.benchmarkId
                && row.build.timestamp === selection.buildTimestamp
                && (!selection.seriesId || row.series.id === selection.seriesId))) {
                throw new CanvasError("selection_not_found", "The requested benchmark/build/series is not present in this analysis");
            }
            return { ...current, selection };
        });
        return { analysisId: saved.analysisId, selection: saved.selection, revision: saved.revision };
    } catch (error) {
        throw canvasError(error, "select_benchmark_failed");
    }
}

async function getState(ctx) {
    try {
        return stateSummary(await store.load(requireInstance(ctx.instanceId).analysisId));
    } catch (error) {
        throw canvasError(error, "get_state_failed");
    }
}

async function clearAnalysis(ctx) {
    try {
        const entry = requireInstance(ctx.instanceId);
        const current = await store.load(entry.analysisId);
        const cleared = createEmptyState(entry.analysisId);
        cleared.revision = current.revision + 1;
        const saved = await store.save(cleared);
        broadcastAnalysis(entry.analysisId, saved);
        return { analysisId: saved.analysisId, cleared: true, revision: saved.revision };
    } catch (error) {
        throw canvasError(error, "clear_analysis_failed");
    }
}

async function exportView(entry, input) {
    const format = input?.format;
    if (!["json", "csv"].includes(format)) {
        const error = new Error("format must be 'json' or 'csv'");
        error.code = "invalid_export_format";
        throw error;
    }
    const state = await store.load(entry.analysisId);
    if (!state.analysis) {
        const error = new Error("Load an analysis before exporting");
        error.code = "analysis_empty";
        throw error;
    }
    const rows = filterRows(state.analysis.rows, state.view);
    let content;
    if (format === "json") {
        content = `${JSON.stringify({
            version: state.version,
            analysisId: state.analysisId,
            metadata: state.analysis.metadata,
            chart: state.view,
            selection: state.selection,
            rows,
        }, null, 2)}\n`;
    } else {
        const fields = [
            "benchmarkId", "benchmarkName", "family", "category", "buildTimestamp", "buildName",
            "seriesId", "seriesLabel", "valueNs", "stddevNs", "stderrNs", "runtimeSha",
            "performanceSha", "status", "sourceUrl", "historyUrl",
        ];
        const quote = (value) => {
            let text = value === undefined || value === null ? "" : String(value);
            if (/^[=+\-@]/.test(text)) {
                text = `'${text}`;
            }
            return `"${text.replaceAll("\"", "\"\"")}"`;
        };
        const records = rows.map((row) => [
            row.benchmark.id, row.benchmark.name, row.benchmark.family, row.benchmark.category,
            row.build.timestamp, row.build.name, row.series.id, row.series.label, row.valueNs,
            row.stddevNs, row.stderrNs, row.runtimeSha, row.performanceSha, row.status,
            row.sourceUrl, row.historyUrl,
        ]);
        content = `${[fields, ...records].map((record) => record.map(quote).join(",")).join("\n")}\n`;
    }
    return {
        artifactPath: await store.writeExport(state.analysisId, format, content),
        format,
        rowCount: rows.length,
    };
}

async function openCanvas(ctx) {
    try {
        const input = ctx.input ?? {};
        const analysisId = validateAnalysisId(input.analysisId ?? "default-analysis");
        let state = await store.load(analysisId);
        if (input.dataArtifactPath && shouldImportArtifact(state)) {
            const analysis = await store.readArtifact(input.dataArtifactPath);
            if (analysis.analysisId !== analysisId) {
                throw new CanvasError("analysis_id_mismatch",
                    `Artifact analysisId '${analysis.analysisId}' does not match open input '${analysisId}'`);
            }
            state = await mutationQueue.run(analysisId, async () => {
                const latest = await store.load(analysisId);
                if (!shouldImportArtifact(latest)) {
                    return latest;
                }
                return store.save({
                    ...latest,
                    analysis,
                    view: analysis.chart,
                    selection: latest.selection,
                    revision: latest.revision + 1,
                });
            });
        }

        let entry = instances.get(ctx.instanceId);
        if (!entry) {
            entry = { analysisId, server: null };
            entry.server = await startCanvasServer({
                getState: () => store.load(entry.analysisId),
                setView: async (inputValue) => {
                    const saved = await mutate(entry, async (current) => ({
                        ...current,
                        view: mergeAndNormalizeChart(current.view, inputValue),
                    }));
                    return stateSummary(saved);
                },
                select: async (inputValue) => {
                    const selection = normalizeSelection(inputValue);
                    const saved = await mutate(entry, async (current) => {
                        if (selection && current.analysis && !current.analysis.rows.some((row) =>
                            row.benchmark.id === selection.benchmarkId
                            && row.build.timestamp === selection.buildTimestamp
                            && (!selection.seriesId || row.series.id === selection.seriesId))) {
                            const error = new Error("The requested benchmark/build/series is not present in this analysis");
                            error.code = "selection_not_found";
                            throw error;
                        }
                        return { ...current, selection };
                    });
                    return { selection: saved.selection, revision: saved.revision };
                },
                ask: async () => {
                    const current = await store.load(entry.analysisId);
                    if (!current.selection) {
                        const error = new Error("Pin a benchmark/build before asking Copilot to inspect it");
                        error.code = "selection_required";
                        throw error;
                    }
                    const selection = current.selection;
                    await session.send({
                        prompt: `Inspect ADX benchmark analysis '${current.analysisId}', benchmark '${selection.benchmarkId}', build '${selection.buildTimestamp}'${selection.seriesId ? `, series '${selection.seriesId}'` : ""}. Run a focused read-only PerformanceData query for deeper provenance or raw-sample context, update the same canonical session artifact, then call set_analysis or append_rows on canvas instance '${ctx.instanceId}'. Do not include credentials or query tokens in canvas state.`,
                    });
                    return { sent: true, analysisId: current.analysisId, selection };
                },
                export: (inputValue) => exportView(entry, inputValue),
            });
            instances.set(ctx.instanceId, entry);
        } else {
            entry.analysisId = analysisId;
        }
        entry.server.broadcast(state);
        return {
            title: state.analysis?.metadata.title ?? "ADX Benchmark Visualizer",
            status: `${state.analysis?.rows.length ?? 0} rows · ${analysisId}`,
            url: entry.server.url,
        };
    } catch (error) {
        throw canvasError(error, "open_failed");
    }
}

session = await joinSession({
    hooks: {
        onSessionStart: async () => ({
            additionalContext: `This project provides the '${CANVAS_ID}' canvas for conversational PerformanceData visualization. The foreground agent, not the iframe, must execute authenticated read-only ADX/Kusto queries. Normalize exact results into the documented version-1 JSON contract under the session workspace, open the canvas with a stable analysisId and optional dataArtifactPath, then refine it with set_analysis, append_rows, set_view, and select_benchmark. Never place credentials, tokens, or arbitrary KQL in canvas state.`,
        }),
    },
    canvases: [
        createCanvas({
            id: CANVAS_ID,
            displayName: "ADX Benchmark Visualizer",
            description: "Render and refine normalized PerformanceData benchmark time series, paired comparisons, ratio distributions, and ranked tables.",
            inputSchema: {
                type: "object",
                additionalProperties: false,
                properties: {
                    analysisId: { type: "string", pattern: "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$" },
                    dataArtifactPath: { type: "string", minLength: 1, maxLength: 512 },
                },
            },
            actions: [
                {
                    name: "set_analysis",
                    description: "Replace the current analysis using a full version-1 analysis object or a session-workspace artifact path.",
                    inputSchema: {
                        type: "object",
                        additionalProperties: false,
                        oneOf: [{ required: ["analysis"] }, { required: ["artifactPath"] }],
                        properties: {
                            analysis: { type: "object" },
                            artifactPath: { type: "string", minLength: 1, maxLength: 512 },
                        },
                    },
                    handler: setAnalysis,
                },
                {
                    name: "append_rows",
                    description: "Validate, append, and deduplicate normalized benchmark rows by the stable benchmark/build/series/run key.",
                    inputSchema: {
                        type: "object",
                        additionalProperties: false,
                        required: ["rows"],
                        properties: { rows: { type: "array", maxItems: 5000, items: { type: "object" } } },
                    },
                    handler: appendRows,
                },
                {
                    name: "set_view",
                    description: "Switch visualization, title, filters, selected series, thresholds, range, top-N, or scatter scale without rewriting rows.",
                    inputSchema: { type: "object" },
                    handler: setView,
                },
                {
                    name: "select_benchmark",
                    description: "Pin a benchmark/build and optional series for exact values, provenance, and follow-up ADX inspection.",
                    inputSchema: {
                        type: "object",
                        additionalProperties: false,
                        required: ["benchmarkId", "buildTimestamp"],
                        properties: {
                            benchmarkId: { type: "string", minLength: 1, maxLength: 256 },
                            buildTimestamp: { type: "string", maxLength: 40 },
                            seriesId: { type: "string", minLength: 1, maxLength: 128 },
                        },
                    },
                    handler: selectBenchmark,
                },
                {
                    name: "get_state",
                    description: "Return a concise analysis, view, series, selection, and revision summary without returning the large row dataset.",
                    inputSchema: { type: "object", additionalProperties: false },
                    handler: getState,
                },
                {
                    name: "clear_analysis",
                    description: "Clear the durable analysis and selection while retaining the stable analysis ID.",
                    inputSchema: { type: "object", additionalProperties: false },
                    handler: clearAnalysis,
                },
            ],
            open: openCanvas,
            onClose: async (ctx) => {
                const entry = instances.get(ctx.instanceId);
                if (entry) {
                    instances.delete(ctx.instanceId);
                    await entry.server.close();
                }
            },
        }),
    ],
});

store = new AnalysisStore(session.workspacePath);
