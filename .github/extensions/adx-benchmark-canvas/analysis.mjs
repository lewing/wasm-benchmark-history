import { normalizeRows } from "./schema.mjs";

const UNIT_FACTORS = {
    ps: 0.001,
    ns: 1,
    "µs": 1_000,
    ms: 1_000_000,
    s: 1_000_000_000,
};

export function stableRowKey(row) {
    return JSON.stringify([
        row.benchmark.id,
        row.build.timestamp,
        row.build.id ?? row.build.name ?? "",
        row.series.id,
        row.series.runId ?? "",
    ]);
}

export function appendDeduplicatedRows(existing, additions, maxRows = 5_000) {
    const normalized = normalizeRows(additions);
    const byKey = new Map(existing.map((row) => [stableRowKey(row), row]));
    let replaced = 0;
    for (const row of normalized) {
        const key = stableRowKey(row);
        if (byKey.has(key)) {
            replaced += 1;
        }
        byKey.set(key, row);
    }
    const rows = [...byKey.values()];
    if (rows.length > maxRows) {
        const error = new Error(`Appending rows would exceed the ${maxRows} row limit`);
        error.code = "oversized_input";
        throw error;
    }
    return { rows, added: normalized.length - replaced, replaced };
}

export function selectDurationUnit(minimumNs, maximumNs = minimumNs) {
    const magnitude = Math.max(Math.abs(minimumNs), Math.abs(maximumNs));
    if (!Number.isFinite(magnitude) || magnitude === 0 || magnitude < 1_000 && magnitude >= 1) {
        return "ns";
    }
    if (magnitude > 0 && magnitude < 1) {
        return "ps";
    }
    if (magnitude < 1_000_000) {
        return "µs";
    }
    if (magnitude < 1_000_000_000) {
        return "ms";
    }
    return "s";
}

export function formatDuration(nanoseconds, unit = selectDurationUnit(nanoseconds), locale = "en-US") {
    if (!Number.isFinite(nanoseconds)) {
        return "unavailable";
    }
    const scaled = nanoseconds / UNIT_FACTORS[unit];
    const magnitude = Math.abs(scaled);
    const decimals = scaled === 0
        ? 0
        : magnitude >= 100
            ? 0
            : magnitude >= 10
                ? 1
                : magnitude >= 1
                    ? 2
                    : Math.min(6, Math.max(3, 2 - Math.floor(Math.log10(magnitude))));
    return `${scaled.toLocaleString(locale, {
        minimumFractionDigits: decimals,
        maximumFractionDigits: decimals,
    })}\u00a0${unit}`;
}

export function formatExactNanoseconds(value, locale = "en-US") {
    if (!Number.isFinite(value)) {
        return "unavailable";
    }
    return `${value.toLocaleString(locale, { maximumSignificantDigits: 15 })}\u00a0ns`;
}

export function filterRows(rows, chart) {
    const filters = chart.filters ?? {};
    const search = (filters.search ?? "").trim().toLocaleLowerCase();
    const selectedSeries = new Set(chart.selectedSeries ?? []);
    const families = new Set(filters.families ?? []);
    const categories = new Set(filters.categories ?? []);
    const statuses = new Set(filters.statuses ?? []);
    const buildNames = new Set(filters.buildNames ?? []);
    const from = chart.range?.from ? Date.parse(chart.range.from) : Number.NEGATIVE_INFINITY;
    const to = chart.range?.to ? Date.parse(chart.range.to) : Number.POSITIVE_INFINITY;
    return rows.filter((row) => {
        const haystack = `${row.benchmark.id} ${row.benchmark.name} ${row.benchmark.family ?? ""} ${row.benchmark.category ?? ""}`.toLocaleLowerCase();
        const time = Date.parse(row.build.timestamp);
        return (!search || haystack.includes(search))
            && (selectedSeries.size === 0 || selectedSeries.has(row.series.id))
            && (families.size === 0 || families.has(row.benchmark.family))
            && (categories.size === 0 || categories.has(row.benchmark.category))
            && (statuses.size === 0 || statuses.has(row.status))
            && (buildNames.size === 0 || buildNames.has(row.build.name))
            && time >= from
            && time <= to;
    });
}

export function derivePairs(rows, baselineSeriesId, candidateSeriesId) {
    if (!baselineSeriesId || !candidateSeriesId || baselineSeriesId === candidateSeriesId) {
        return [];
    }
    const groups = new Map();
    for (const row of rows) {
        const key = JSON.stringify([
            row.benchmark.id,
            row.build.timestamp,
            row.build.id ?? row.build.name ?? "",
        ]);
        const group = groups.get(key) ?? {};
        if (row.series.id === baselineSeriesId) {
            group.baseline = row;
        } else if (row.series.id === candidateSeriesId) {
            group.candidate = row;
        }
        groups.set(key, group);
    }
    return [...groups.values()]
        .filter(({ baseline, candidate }) =>
            baseline?.status === "valid"
            && candidate?.status === "valid"
            && baseline.valueNs > 0
            && candidate.valueNs >= 0)
        .map(({ baseline, candidate }) => ({
            key: JSON.stringify([baseline.benchmark.id, baseline.build.timestamp]),
            benchmarkId: baseline.benchmark.id,
            benchmarkName: baseline.benchmark.name,
            family: baseline.benchmark.family,
            category: baseline.benchmark.category,
            buildTimestamp: baseline.build.timestamp,
            buildName: baseline.build.name,
            baseline,
            candidate,
            baselineNs: baseline.valueNs,
            candidateNs: candidate.valueNs,
            ratio: candidate.valueNs / baseline.valueNs,
        }));
}

export function buildRatioHistogram(pairs, binCount = 20) {
    const ratios = pairs.map((pair) => pair.ratio).filter((ratio) => Number.isFinite(ratio) && ratio >= 0);
    if (ratios.length === 0) {
        return { bins: [], minimum: null, maximum: null };
    }
    const minimum = Math.min(...ratios);
    const maximum = Math.max(...ratios);
    const width = maximum === minimum ? Math.max(0.01, minimum * 0.01) : (maximum - minimum) / binCount;
    const start = maximum === minimum ? Math.max(0, minimum - width / 2) : minimum;
    const bins = Array.from({ length: binCount }, (_, index) => ({
        from: start + index * width,
        to: start + (index + 1) * width,
        count: 0,
    }));
    for (const ratio of ratios) {
        const index = Math.min(binCount - 1, Math.floor((ratio - start) / width));
        bins[Math.max(0, index)].count += 1;
    }
    return { bins, minimum, maximum };
}

export function summarizeRatios(pairs, thresholds = { slowdownRatio: 1.05, speedupRatio: 0.95 }) {
    const result = { total: pairs.length, slowdowns: 0, speedups: 0, neutral: 0 };
    for (const pair of pairs) {
        if (pair.ratio >= thresholds.slowdownRatio) {
            result.slowdowns += 1;
        } else if (pair.ratio <= thresholds.speedupRatio) {
            result.speedups += 1;
        } else {
            result.neutral += 1;
        }
    }
    return result;
}

export function stateSummary(state) {
    const rows = state.analysis?.rows ?? [];
    return {
        version: state.version,
        analysisId: state.analysisId,
        title: state.analysis?.metadata.title ?? null,
        visualization: state.view.visualization,
        rowCount: rows.length,
        series: [...new Map(rows.map((row) => [row.series.id, row.series.label])).entries()]
            .map(([id, label]) => ({ id, label })),
        filters: state.view.filters,
        baselineSeriesId: state.view.baselineSeriesId ?? null,
        selectedSeries: state.view.selectedSeries,
        selection: state.selection,
        revision: state.revision,
    };
}
