export const CONTRACT_VERSION = 1;
export const MAX_ROWS = 5_000;
export const MAX_SAMPLES_PER_ROW = 200;
export const MAX_TOTAL_SAMPLES = 20_000;
export const MAX_TEXT = 512;
export const MAX_URL = 2_048;
export const VISUALIZATIONS = [
    "time-series",
    "paired-scatter",
    "ratio-distribution",
    "ranked-table",
];
export const ROW_STATUSES = ["valid", "invalid", "missing", "excluded"];

const ANALYSIS_ID = /^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$/;
const SHA = /^[0-9a-f]{7,64}$/i;
const TIMESTAMP = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?Z$/;

export class ValidationError extends Error {
    constructor(code, message) {
        super(message);
        this.code = code;
    }
}

function fail(code, path, message) {
    throw new ValidationError(code, `${path}: ${message}`);
}

function object(value, path) {
    if (value === null || typeof value !== "object" || Array.isArray(value)) {
        fail("invalid_type", path, "must be an object");
    }
    return value;
}

function strictKeys(value, allowed, path) {
    for (const key of Object.keys(value)) {
        if (!allowed.includes(key)) {
            fail("unknown_field", `${path}.${key}`, "is not supported");
        }
    }
}

function string(value, path, { max = MAX_TEXT, pattern, allowEmpty = false } = {}) {
    if (typeof value !== "string") {
        fail("invalid_type", path, "must be a string");
    }
    if ((!allowEmpty && value.length === 0) || value.length > max) {
        fail("invalid_text", path, `must contain ${allowEmpty ? "0" : "1"}-${max} characters`);
    }
    if (pattern && !pattern.test(value)) {
        fail("invalid_text", path, "has an invalid format");
    }
    return value;
}

function optionalString(value, path, options) {
    return value === undefined ? undefined : string(value, path, options);
}

function finiteNumber(value, path, { minimum, maximum } = {}) {
    if (typeof value !== "number" || !Number.isFinite(value)) {
        fail("invalid_number", path, "must be a finite number");
    }
    if (minimum !== undefined && value < minimum) {
        fail("invalid_number", path, `must be at least ${minimum}`);
    }
    if (maximum !== undefined && value > maximum) {
        fail("invalid_number", path, `must be at most ${maximum}`);
    }
    return value;
}

function optionalNumber(value, path, options) {
    return value === undefined ? undefined : finiteNumber(value, path, options);
}

function integer(value, path, minimum, maximum) {
    finiteNumber(value, path, { minimum, maximum });
    if (!Number.isInteger(value)) {
        fail("invalid_number", path, "must be an integer");
    }
    return value;
}

function boolean(value, path) {
    if (typeof value !== "boolean") {
        fail("invalid_type", path, "must be a boolean");
    }
    return value;
}

function optionalBoolean(value, path) {
    return value === undefined ? undefined : boolean(value, path);
}

function enumeration(value, values, path) {
    if (!values.includes(value)) {
        fail("invalid_value", path, `must be one of: ${values.join(", ")}`);
    }
    return value;
}

function timestamp(value, path) {
    string(value, path, { max: 40, pattern: TIMESTAMP });
    if (!Number.isFinite(Date.parse(value))) {
        fail("invalid_timestamp", path, "must be a valid UTC ISO-8601 timestamp");
    }
    return value;
}

export function validateAnalysisId(value, path = "analysisId") {
    return string(value, path, { max: 128, pattern: ANALYSIS_ID });
}

export function validateSafeUrl(value, path = "url") {
    string(value, path, { max: MAX_URL });
    let parsed;
    try {
        parsed = new URL(value);
    } catch {
        fail("unsafe_url", path, "must be a valid absolute HTTPS URL");
    }
    if (parsed.protocol !== "https:" || parsed.username || parsed.password) {
        fail("unsafe_url", path, "must be HTTPS and must not contain credentials");
    }
    if (!parsed.hostname || !isPublicHostname(parsed.hostname)) {
        fail("unsafe_url", path, "must use a public hostname");
    }
    return parsed.href;
}

function isPublicHostname(hostname) {
    const normalized = hostname.toLocaleLowerCase().replace(/^\[|\]$/g, "");
    if (normalized === "localhost" || normalized.endsWith(".localhost") || normalized.endsWith(".local")) {
        return false;
    }
    const version = isIP(normalized);
    if (version === 4) {
        const [a, b, c] = normalized.split(".").map(Number);
        return !(a === 0
            || a === 10
            || a === 127
            || (a === 100 && b >= 64 && b <= 127)
            || (a === 169 && b === 254)
            || (a === 172 && b >= 16 && b <= 31)
            || (a === 192 && b === 0 && (c === 0 || c === 2))
            || (a === 192 && b === 168)
            || (a === 198 && (b === 18 || b === 19))
            || (a === 198 && b === 51 && c === 100)
            || (a === 203 && b === 0 && c === 113)
            || a >= 224);
    }
    if (version === 6) {
        if (normalized.startsWith("::")
            || normalized.startsWith("fc") || normalized.startsWith("fd")
            || /^fe[89ab]/.test(normalized)
            || normalized.startsWith("ff")
            || normalized.startsWith("2001:db8:")) {
            return false;
        }
    }
    return true;
}

function stringArray(value, path, { maxItems = 100, itemMax = 128, allowed } = {}) {
    if (!Array.isArray(value) || value.length > maxItems) {
        fail("invalid_array", path, `must be an array with at most ${maxItems} items`);
    }
    return [...new Set(value.map((item, index) => {
        const normalized = string(item, `${path}[${index}]`, { max: itemMax });
        return allowed ? enumeration(normalized, allowed, `${path}[${index}]`) : normalized;
    }))];
}

function normalizeRunConfiguration(value, path) {
    if (value === undefined) {
        return undefined;
    }
    object(value, path);
    const entries = Object.entries(value);
    if (entries.length > 24) {
        fail("oversized_input", path, "must contain at most 24 entries");
    }
    const result = {};
    for (const [key, item] of entries) {
        const normalizedKey = string(key, `${path} key`, { max: 80 });
        result[normalizedKey] = string(item, `${path}.${key}`, { max: 160, allowEmpty: true });
    }
    return result;
}

function normalizeFilters(value, path) {
    if (value === undefined) {
        return {};
    }
    object(value, path);
    strictKeys(value, ["search", "families", "categories", "statuses", "buildNames"], path);
    return compact({
        search: optionalString(value.search, `${path}.search`, { max: 160, allowEmpty: true }),
        families: value.families === undefined ? undefined : stringArray(value.families, `${path}.families`),
        categories: value.categories === undefined ? undefined : stringArray(value.categories, `${path}.categories`),
        statuses: value.statuses === undefined
            ? undefined
            : stringArray(value.statuses, `${path}.statuses`, { allowed: ROW_STATUSES }),
        buildNames: value.buildNames === undefined ? undefined : stringArray(value.buildNames, `${path}.buildNames`),
    });
}

function normalizeRange(value, path) {
    if (value === undefined) {
        return undefined;
    }
    object(value, path);
    strictKeys(value, ["from", "to"], path);
    const from = value.from === undefined ? undefined : timestamp(value.from, `${path}.from`);
    const to = value.to === undefined ? undefined : timestamp(value.to, `${path}.to`);
    if (from && to && Date.parse(from) > Date.parse(to)) {
        fail("invalid_range", path, "from must not be after to");
    }
    return compact({ from, to });
}

function normalizeThresholds(value, path) {
    if (value === undefined) {
        return { slowdownRatio: 1.05, speedupRatio: 0.95 };
    }
    object(value, path);
    strictKeys(value, ["slowdownRatio", "speedupRatio"], path);
    const slowdownRatio = value.slowdownRatio === undefined
        ? 1.05
        : finiteNumber(value.slowdownRatio, `${path}.slowdownRatio`, { minimum: 1, maximum: 100 });
    const speedupRatio = value.speedupRatio === undefined
        ? 0.95
        : finiteNumber(value.speedupRatio, `${path}.speedupRatio`, { minimum: 0, maximum: 1 });
    return { slowdownRatio, speedupRatio };
}

export function normalizeChartSpec(value, path = "chart") {
    object(value, path);
    strictKeys(value, [
        "title",
        "subtitle",
        "visualization",
        "xSemantic",
        "ySemantic",
        "unitKind",
        "filters",
        "selectedSeries",
        "baselineSeriesId",
        "thresholds",
        "topN",
        "logScale",
        "range",
    ], path);
    const visualization = value.visualization === undefined
        ? "time-series"
        : enumeration(value.visualization, VISUALIZATIONS, `${path}.visualization`);
    return compact({
        title: optionalString(value.title, `${path}.title`, { max: 200 }),
        subtitle: optionalString(value.subtitle, `${path}.subtitle`, { max: 300, allowEmpty: true }),
        visualization,
        xSemantic: value.xSemantic === undefined
            ? defaultX(visualization)
            : enumeration(value.xSemantic, ["build-time", "baseline-value", "ratio", "benchmark"], `${path}.xSemantic`),
        ySemantic: value.ySemantic === undefined
            ? defaultY(visualization)
            : enumeration(value.ySemantic, ["value-ns", "candidate-value", "count", "ratio"], `${path}.ySemantic`),
        unitKind: value.unitKind === undefined
            ? (visualization === "ratio-distribution" ? "ratio" : "duration-ns")
            : enumeration(value.unitKind, ["duration-ns", "ratio"], `${path}.unitKind`),
        filters: normalizeFilters(value.filters, `${path}.filters`),
        selectedSeries: value.selectedSeries === undefined
            ? []
            : stringArray(value.selectedSeries, `${path}.selectedSeries`, { maxItems: 32 }),
        baselineSeriesId: optionalString(value.baselineSeriesId, `${path}.baselineSeriesId`, { max: 128 }),
        thresholds: normalizeThresholds(value.thresholds, `${path}.thresholds`),
        topN: value.topN === undefined ? 50 : integer(value.topN, `${path}.topN`, 1, 500),
        logScale: value.logScale === undefined ? false : boolean(value.logScale, `${path}.logScale`),
        range: normalizeRange(value.range, `${path}.range`),
    });
}

function defaultX(visualization) {
    return {
        "time-series": "build-time",
        "paired-scatter": "baseline-value",
        "ratio-distribution": "ratio",
        "ranked-table": "benchmark",
    }[visualization];
}

function defaultY(visualization) {
    return {
        "time-series": "value-ns",
        "paired-scatter": "candidate-value",
        "ratio-distribution": "count",
        "ranked-table": "ratio",
    }[visualization];
}

function normalizeMetadata(value, path) {
    object(value, path);
    strictKeys(value, [
        "title",
        "description",
        "createdAt",
        "updatedAt",
        "querySummary",
        "dataSource",
        "sourceUrl",
    ], path);
    return compact({
        title: string(value.title, `${path}.title`, { max: 200 }),
        description: optionalString(value.description, `${path}.description`, { max: 500, allowEmpty: true }),
        createdAt: timestamp(value.createdAt, `${path}.createdAt`),
        updatedAt: value.updatedAt === undefined ? undefined : timestamp(value.updatedAt, `${path}.updatedAt`),
        querySummary: optionalString(value.querySummary, `${path}.querySummary`, { max: 500, allowEmpty: true }),
        dataSource: value.dataSource === undefined
            ? "PerformanceData"
            : enumeration(value.dataSource, ["PerformanceData"], `${path}.dataSource`),
        sourceUrl: value.sourceUrl === undefined ? undefined : validateSafeUrl(value.sourceUrl, `${path}.sourceUrl`),
    });
}

function normalizeRow(value, index, sampleCounter) {
    const path = `rows[${index}]`;
    object(value, path);
    strictKeys(value, [
        "benchmark",
        "build",
        "series",
        "valueNs",
        "stddevNs",
        "stderrNs",
        "medianNs",
        "q1Ns",
        "q3Ns",
        "rawSamplesNs",
        "runtimeSha",
        "performanceSha",
        "runConfiguration",
        "sourceUrl",
        "historyUrl",
        "validity",
        "status",
    ], path);

    object(value.benchmark, `${path}.benchmark`);
    strictKeys(value.benchmark, ["id", "name", "family", "category"], `${path}.benchmark`);
    object(value.build, `${path}.build`);
    strictKeys(value.build, ["timestamp", "name", "id", "marker"], `${path}.build`);
    object(value.series, `${path}.series`);
    strictKeys(value.series, ["id", "label", "runId"], `${path}.series`);

    let rawSamplesNs;
    if (value.rawSamplesNs !== undefined) {
        if (!Array.isArray(value.rawSamplesNs) || value.rawSamplesNs.length > MAX_SAMPLES_PER_ROW) {
            fail("oversized_input", `${path}.rawSamplesNs`, `must contain at most ${MAX_SAMPLES_PER_ROW} values`);
        }
        sampleCounter.count += value.rawSamplesNs.length;
        if (sampleCounter.count > MAX_TOTAL_SAMPLES) {
            fail("oversized_input", "rows", `must contain at most ${MAX_TOTAL_SAMPLES} raw samples total`);
        }
        rawSamplesNs = value.rawSamplesNs.map((sample, sampleIndex) =>
            finiteNumber(sample, `${path}.rawSamplesNs[${sampleIndex}]`, { minimum: 0 }));
    }

    const status = value.status === undefined
        ? (value.validity === false ? "invalid" : "valid")
        : enumeration(value.status, ROW_STATUSES, `${path}.status`);

    return compact({
        benchmark: compact({
            id: string(value.benchmark.id, `${path}.benchmark.id`, { max: 256 }),
            name: string(value.benchmark.name, `${path}.benchmark.name`, { max: 300 }),
            family: optionalString(value.benchmark.family, `${path}.benchmark.family`, { max: 160 }),
            category: optionalString(value.benchmark.category, `${path}.benchmark.category`, { max: 160 }),
        }),
        build: compact({
            timestamp: timestamp(value.build.timestamp, `${path}.build.timestamp`),
            name: optionalString(value.build.name, `${path}.build.name`, { max: 160 }),
            id: optionalString(value.build.id, `${path}.build.id`, { max: 160 }),
            marker: optionalString(value.build.marker, `${path}.build.marker`, { max: 160 }),
        }),
        series: compact({
            id: string(value.series.id, `${path}.series.id`, { max: 128 }),
            label: string(value.series.label, `${path}.series.label`, { max: 160 }),
            runId: optionalString(value.series.runId, `${path}.series.runId`, { max: 160 }),
        }),
        valueNs: finiteNumber(value.valueNs, `${path}.valueNs`, { minimum: 0 }),
        stddevNs: optionalNumber(value.stddevNs, `${path}.stddevNs`, { minimum: 0 }),
        stderrNs: optionalNumber(value.stderrNs, `${path}.stderrNs`, { minimum: 0 }),
        medianNs: optionalNumber(value.medianNs, `${path}.medianNs`, { minimum: 0 }),
        q1Ns: optionalNumber(value.q1Ns, `${path}.q1Ns`, { minimum: 0 }),
        q3Ns: optionalNumber(value.q3Ns, `${path}.q3Ns`, { minimum: 0 }),
        rawSamplesNs,
        runtimeSha: optionalString(value.runtimeSha, `${path}.runtimeSha`, { max: 64, pattern: SHA }),
        performanceSha: optionalString(value.performanceSha, `${path}.performanceSha`, { max: 64, pattern: SHA }),
        runConfiguration: normalizeRunConfiguration(value.runConfiguration, `${path}.runConfiguration`),
        sourceUrl: value.sourceUrl === undefined ? undefined : validateSafeUrl(value.sourceUrl, `${path}.sourceUrl`),
        historyUrl: value.historyUrl === undefined ? undefined : validateSafeUrl(value.historyUrl, `${path}.historyUrl`),
        validity: value.validity === undefined ? status === "valid" : boolean(value.validity, `${path}.validity`),
        status,
    });
}

export function normalizeAnalysis(value) {
    object(value, "analysis");
    strictKeys(value, ["version", "analysisId", "metadata", "chart", "rows"], "analysis");
    if (value.version !== CONTRACT_VERSION) {
        fail("unsupported_version", "analysis.version", `must equal ${CONTRACT_VERSION}`);
    }
    if (!Array.isArray(value.rows) || value.rows.length > MAX_ROWS) {
        fail("oversized_input", "analysis.rows", `must be an array with at most ${MAX_ROWS} rows`);
    }
    const sampleCounter = { count: 0 };
    return {
        version: CONTRACT_VERSION,
        analysisId: validateAnalysisId(value.analysisId, "analysis.analysisId"),
        metadata: normalizeMetadata(value.metadata, "analysis.metadata"),
        chart: normalizeChartSpec(value.chart, "analysis.chart"),
        rows: value.rows.map((row, index) => normalizeRow(row, index, sampleCounter)),
    };
}

export function normalizeRows(value) {
    if (!Array.isArray(value) || value.length > MAX_ROWS) {
        fail("oversized_input", "rows", `must be an array with at most ${MAX_ROWS} rows`);
    }
    const sampleCounter = { count: 0 };
    return value.map((row, index) => normalizeRow(row, index, sampleCounter));
}

export function normalizeSelection(value, path = "selection") {
    if (value === null || value === undefined) {
        return null;
    }
    object(value, path);
    strictKeys(value, ["benchmarkId", "buildTimestamp", "seriesId"], path);
    return {
        benchmarkId: string(value.benchmarkId, `${path}.benchmarkId`, { max: 256 }),
        buildTimestamp: timestamp(value.buildTimestamp, `${path}.buildTimestamp`),
        seriesId: optionalString(value.seriesId, `${path}.seriesId`, { max: 128 }),
    };
}

export function mergeAndNormalizeChart(current, patch) {
    object(patch, "view");
    const merged = {
        ...current,
        ...patch,
        filters: patch.filters === undefined ? current.filters : { ...current.filters, ...patch.filters },
        thresholds: patch.thresholds === undefined
            ? current.thresholds
            : { ...current.thresholds, ...patch.thresholds },
    };
    return normalizeChartSpec(merged, "view");
}

function compact(value) {
    return Object.fromEntries(Object.entries(value).filter(([, item]) => item !== undefined));
}
import { isIP } from "node:net";
